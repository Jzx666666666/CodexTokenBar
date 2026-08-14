using System.IO;

namespace CodexTokenBar.Codex;

public interface IAppServerConnection : IAsyncDisposable
{
    TextWriter Input { get; }
    TextReader Output { get; }
    TextReader Error { get; }
    int ProcessId { get; }
    bool HasExited { get; }
    Task<string> ReadDiagnosticAsync(CancellationToken cancellationToken);
    Task WaitForExitAsync(CancellationToken cancellationToken);
    Task TerminateAsync(CancellationToken cancellationToken);
}

public sealed class CodexProtocolException : Exception
{
    public CodexProtocolException(string message) : base(message)
    {
    }

    public CodexProtocolException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
