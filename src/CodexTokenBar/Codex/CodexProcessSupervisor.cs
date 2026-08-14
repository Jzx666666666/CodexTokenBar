using System.Diagnostics;
using System.IO;

namespace CodexTokenBar.Codex;

public sealed class CodexProcessSupervisor : IAsyncDisposable
{
    private readonly string _executablePath;
    private readonly IReadOnlyList<string>? _explicitArguments;
    private readonly Func<string, CancellationToken, Task>? _diagnosticSink;

    public CodexProcessSupervisor(
        string executablePath,
        IReadOnlyList<string>? explicitArguments = null,
        Func<string, CancellationToken, Task>? diagnosticSink = null)
    {
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        _explicitArguments = explicitArguments;
        _diagnosticSink = diagnosticSink;
    }

    public Task<IAppServerConnection> StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = _explicitArguments is null
            ? CreateStartInfo(_executablePath)
            : CreateDirectStartInfo(_executablePath, _explicitArguments);
        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        try
        {
            if (!process.Start())
                throw new CodexProcessException("无法启动 Codex app-server");
            return Task.FromResult<IAppServerConnection>(new ProcessAppServerConnection(process, _diagnosticSink));
        }
        catch (Exception exception) when (exception is not CodexProcessException)
        {
            process.Dispose();
            throw new CodexProcessException("无法启动 Codex app-server", exception);
        }
    }

    public static ProcessStartInfo CreateStartInfo(string commandPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandPath);
        if (string.Equals(Path.GetExtension(commandPath), ".cmd", StringComparison.OrdinalIgnoreCase))
        {
            var info = BaseStartInfo(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe");
            info.Arguments = $"/d /s /c \"\"{commandPath}\" app-server\"";
            return info;
        }

        return CreateDirectStartInfo(commandPath, ["app-server"]);
    }

    private static ProcessStartInfo CreateDirectStartInfo(
        string executablePath,
        IEnumerable<string> arguments)
    {
        var info = BaseStartInfo(executablePath);
        foreach (var argument in arguments)
            info.ArgumentList.Add(argument);
        return info;
    }

    private static ProcessStartInfo BaseStartInfo(string fileName) => new()
    {
        FileName = fileName,
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        StandardInputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        StandardOutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
        StandardErrorEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
    };

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private sealed class ProcessAppServerConnection : IAppServerConnection
    {
        private readonly Process _process;
        private readonly Func<string, CancellationToken, Task>? _diagnosticSink;
        private readonly CancellationTokenSource _diagnosticLifetime = new();
        private readonly Task? _diagnosticLoop;
        private readonly List<string> _recentDiagnostics = [];
        private readonly object _diagnosticGate = new();
        private int _disposed;

        public ProcessAppServerConnection(
            Process process,
            Func<string, CancellationToken, Task>? diagnosticSink)
        {
            _process = process;
            _diagnosticSink = diagnosticSink;
            _diagnosticLoop = DrainDiagnosticsAsync(_diagnosticLifetime.Token);
        }

        public TextWriter Input => _process.StandardInput;
        public TextReader Output => _process.StandardOutput;
        public TextReader Error => _process.StandardError;
        public int ProcessId => _process.Id;
        public bool HasExited => _process.HasExited;

        public async Task<string> ReadDiagnosticAsync(CancellationToken cancellationToken)
        {
            if (!_process.HasExited)
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromMilliseconds(250));
                try
                {
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                }
            }

            if (_diagnosticLoop is not null && _process.HasExited)
                await _diagnosticLoop.ConfigureAwait(false);
            lock (_diagnosticGate)
                return string.Join(" | ", _recentDiagnostics);
        }

        private async Task DrainDiagnosticsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested &&
                       await Error.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
                {
                    lock (_diagnosticGate)
                    {
                        _recentDiagnostics.Add(line);
                        if (_recentDiagnostics.Count > 8)
                            _recentDiagnostics.RemoveAt(0);
                    }
                    if (_diagnosticSink is not null)
                    {
                        try
                        {
                            await _diagnosticSink(line, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch
            {
            }
        }

        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            _process.WaitForExitAsync(cancellationToken);

        public Task TerminateAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
                return;

            try
            {
                _process.StandardInput.Close();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                try
                {
                    await _process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    if (!_process.HasExited)
                    {
                        _process.Kill(entireProcessTree: true);
                        await _process.WaitForExitAsync().ConfigureAwait(false);
                    }
                }
            }
            finally
            {
                _diagnosticLifetime.Cancel();
                if (_diagnosticLoop is not null)
                {
                    try
                    {
                        await _diagnosticLoop.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }
                _diagnosticLifetime.Dispose();
                _process.Dispose();
            }
        }
    }
}
