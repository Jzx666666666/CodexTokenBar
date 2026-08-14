using System.IO;
using System.Text.Json;

namespace CodexTokenBar.Persistence;

public sealed class AppStateStore : IAppStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly AppPaths _paths;
    private readonly SemaphoreSlim _ioLock = new(1, 1);

    public AppStateStore(AppPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken) =>
        await ReadAsync(_paths.SettingsFile, AppSettings.Default, cancellationToken).ConfigureAwait(false);

    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken) =>
        WriteAtomicAsync(_paths.SettingsFile, settings, cancellationToken);

    public Task<StoredQuotaSnapshot?> LoadLastSnapshotAsync(CancellationToken cancellationToken) =>
        ReadAsync<StoredQuotaSnapshot?>(_paths.SnapshotFile, null, cancellationToken);

    public Task SaveLastSnapshotAsync(StoredQuotaSnapshot snapshot, CancellationToken cancellationToken) =>
        WriteAtomicAsync(_paths.SnapshotFile, snapshot, cancellationToken);

    private async Task<T> ReadAsync<T>(string path, T fallback, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(path))
                return fallback;

            try
            {
                await using var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read, FileShare.Read,
                    bufferSize: 4_096, useAsync: true);
                return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
                    .ConfigureAwait(false) ?? fallback;
            }
            catch (JsonException)
            {
                return fallback;
            }
            catch (IOException)
            {
                return fallback;
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }

    private async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptions);
            await using (var stream = new FileStream(
                temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: 4_096, useAsync: true))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
            _ioLock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _ioLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var ownedFiles = new List<string>
            {
                _paths.SettingsFile,
                _paths.SettingsFile + ".tmp",
                _paths.SnapshotFile,
                _paths.SnapshotFile + ".tmp",
                _paths.LogFile,
            };
            ownedFiles.AddRange(Enumerable.Range(1, 6).Select(_paths.RotatedLogFile));
            foreach (var file in ownedFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
        finally
        {
            _ioLock.Release();
        }
    }
}
