using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace CodexTokenBar.Windows;

public sealed class SingleInstanceCoordinator : IAsyncDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();
    private Mutex? _mutex;
    private Task? _acceptLoop;
    private int _disposed;

    public SingleInstanceCoordinator(string? suffix = null, string? userIdentity = null)
    {
        var user = userIdentity ?? WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)))[..16];
        var safeSuffix = string.IsNullOrWhiteSpace(suffix) ? "main" : suffix;
        _mutexName = $"Local\\CodexTokenBar-{hash}-{safeSuffix}";
        _pipeName = $"CodexTokenBar-{hash}-{safeSuffix}";
    }

    public event Action? ActivationRequested;

    public Task<bool> TryAcquireAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (_mutex is not null)
            return Task.FromResult(true);

        var mutex = new Mutex(initiallyOwned: false, _mutexName, out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return Task.FromResult(false);
        }

        _mutex = mutex;
        _acceptLoop = AcceptLoopAsync(_lifetime.Token);
        return Task.FromResult(true);
    }

    public Task SendShowAsync(CancellationToken cancellationToken) =>
        SendAsync("show", cancellationToken);

    public async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        await using var client = new NamedPipeClientStream(
            ".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous);
        await client.ConnectAsync(3_000, cancellationToken).ConfigureAwait(false);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: false)
        {
            AutoFlush = true,
        };
        await writer.WriteLineAsync(command.AsMemory(), cancellationToken).ConfigureAwait(false);
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.In,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
                var command = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (string.Equals(command, "show", StringComparison.Ordinal))
                    ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (IOException)
            {
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _lifetime.Cancel();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_mutex is not null)
        {
            _mutex.Dispose();
        }
        _lifetime.Dispose();
    }
}
