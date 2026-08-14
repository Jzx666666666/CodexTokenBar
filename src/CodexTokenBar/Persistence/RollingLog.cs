using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexTokenBar.Persistence;

public sealed partial class RollingLog
{
    private readonly AppPaths _paths;
    private readonly long _maxBytes;
    private readonly int _retainedFiles;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public RollingLog(AppPaths paths, long maxBytes = 1_048_576, int retainedFiles = 7)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        if (maxBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (retainedFiles is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(retainedFiles));
        _maxBytes = maxBytes;
        _retainedFiles = retainedFiles;
    }

    public async Task WriteAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var safeMessage = SensitiveAssignment().Replace(
            message.ReplaceLineEndings(" "),
            match => $"{match.Groups["key"].Value}{match.Groups["separator"].Value}[REDACTED]");
        var line = $"{DateTimeOffset.Now:O} {safeMessage}{Environment.NewLine}";
        var bytes = Encoding.UTF8.GetBytes(line);

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.RootDirectory);
            var currentLength = File.Exists(_paths.LogFile)
                ? new FileInfo(_paths.LogFile).Length
                : 0;
            if (currentLength > 0 && currentLength + bytes.Length > _maxBytes)
                Rotate();
            await File.AppendAllTextAsync(_paths.LogFile, line, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private void Rotate()
    {
        var oldest = _retainedFiles - 1;
        if (oldest > 0 && File.Exists(_paths.RotatedLogFile(oldest)))
            File.Delete(_paths.RotatedLogFile(oldest));

        for (var index = oldest - 1; index >= 1; index--)
        {
            var source = _paths.RotatedLogFile(index);
            if (File.Exists(source))
                File.Move(source, _paths.RotatedLogFile(index + 1), overwrite: true);
        }

        if (_retainedFiles > 1 && File.Exists(_paths.LogFile))
            File.Move(_paths.LogFile, _paths.RotatedLogFile(1), overwrite: true);
        else if (File.Exists(_paths.LogFile))
            File.Delete(_paths.LogFile);
    }

    [GeneratedRegex(
        "(?<key>[A-Za-z0-9_.-]*(?:token|authorization|cookie|auth)[A-Za-z0-9_.-]*)(?<separator>\\s*[:=]\\s*).*?(?=\\s+[A-Za-z0-9_.-]+\\s*[:=]|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SensitiveAssignment();
}
