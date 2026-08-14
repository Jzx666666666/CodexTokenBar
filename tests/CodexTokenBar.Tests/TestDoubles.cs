using System.Collections.Concurrent;
using System.Text;
using System.Threading.Channels;
using CodexTokenBar.Codex;

namespace CodexTokenBar.Tests;

internal sealed class ScriptedAppServerConnection : IAppServerConnection
{
    private readonly Channel<string?> _output = Channel.CreateUnbounded<string?>();
    private readonly Channel<string> _input = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _exited = new();

    public ScriptedAppServerConnection()
    {
        Input = new RecordingLineWriter(_input.Writer);
        Output = new ChannelLineReader(_output.Reader);
        Error = TextReader.Null;
    }

    public TextWriter Input { get; }
    public TextReader Output { get; }
    public TextReader Error { get; }
    public int ProcessId => 42;
    public bool HasExited => _exited.IsCancellationRequested;
    public bool IsDisposed { get; private set; }

    public Task<string> ReadDiagnosticAsync(CancellationToken cancellationToken) =>
        Task.FromResult(string.Empty);

    public ValueTask FeedLineAsync(string line) => _output.Writer.WriteAsync(line);

    public void CompleteOutput() => _output.Writer.TryComplete();

    public async Task<string> ReadWrittenLineAsync(CancellationToken cancellationToken = default) =>
        await _input.Reader.ReadAsync(cancellationToken);

    public Task WaitForExitAsync(CancellationToken cancellationToken) =>
        Task.Delay(Timeout.InfiniteTimeSpan, CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _exited.Token).Token);

    public Task TerminateAsync(CancellationToken cancellationToken)
    {
        _exited.Cancel();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        IsDisposed = true;
        _exited.Cancel();
        _output.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private sealed class RecordingLineWriter(ChannelWriter<string> lines) : TextWriter
    {
        private readonly StringBuilder _buffer = new();
        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value)
        {
            if (value == '\n')
            {
                lines.TryWrite(_buffer.ToString().TrimEnd('\r'));
                _buffer.Clear();
            }
            else
            {
                _buffer.Append(value);
            }
        }

        public override Task WriteAsync(char value)
        {
            Write(value);
            return Task.CompletedTask;
        }

        public override Task FlushAsync() => Task.CompletedTask;
    }

    private sealed class ChannelLineReader(ChannelReader<string?> lines) : TextReader
    {
        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                return await lines.ReadAsync(cancellationToken);
            }
            catch (ChannelClosedException)
            {
                return null;
            }
        }
    }
}

internal sealed class ConnectionFactory
{
    private readonly ConcurrentQueue<ScriptedAppServerConnection> _connections = new();
    public int CreationCount { get; private set; }

    public void Enqueue(ScriptedAppServerConnection connection) => _connections.Enqueue(connection);

    public Task<IAppServerConnection> CreateAsync(CancellationToken cancellationToken)
    {
        CreationCount++;
        if (!_connections.TryDequeue(out var connection))
            throw new InvalidOperationException("No scripted connection is available.");
        return Task.FromResult<IAppServerConnection>(connection);
    }
}
