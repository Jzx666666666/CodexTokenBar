namespace CodexTokenBar.Codex;

public sealed class CodexConnectionFactory
{
    private readonly IReadOnlyList<string> _commands;
    private readonly Func<string, CancellationToken, Task<IAppServerConnection>> _starter;
    private readonly Action<int>? _processStarted;

    public CodexConnectionFactory(
        IReadOnlyList<string> commands,
        Func<string, CancellationToken, Task<IAppServerConnection>>? starter = null,
        Func<string, CancellationToken, Task>? diagnosticSink = null,
        Action<int>? processStarted = null)
    {
        ArgumentNullException.ThrowIfNull(commands);
        if (commands.Count == 0)
            throw new ArgumentException("至少需要一个 Codex CLI 候选命令", nameof(commands));
        _commands = commands;
        _starter = starter ?? ((command, cancellationToken) =>
            StartProcessAsync(command, diagnosticSink, cancellationToken));
        _processStarted = processStarted;
    }

    public async Task<IAppServerConnection> StartAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var command in _commands)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var connection = await _starter(command, cancellationToken).ConfigureAwait(false);
                _processStarted?.Invoke(connection.ProcessId);
                return connection;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        throw new CodexProcessException(
            "无法启动任何已发现的 Codex CLI",
            new AggregateException(failures));
    }

    private static async Task<IAppServerConnection> StartProcessAsync(
        string command,
        Func<string, CancellationToken, Task>? diagnosticSink,
        CancellationToken cancellationToken)
    {
        await using var supervisor = new CodexProcessSupervisor(command, diagnosticSink: diagnosticSink);
        return await supervisor.StartAsync(cancellationToken).ConfigureAwait(false);
    }
}
