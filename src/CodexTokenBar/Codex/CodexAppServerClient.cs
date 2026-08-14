using System.Collections.Concurrent;
using System.Text.Json;

namespace CodexTokenBar.Codex;

public sealed class CodexAppServerClient : IAsyncDisposable
{
    private readonly IAppServerConnection _connection;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readLoop;
    private int _nextRequestId;
    private int _disposed;
    private bool _initialized;

    public CodexAppServerClient(
        IAppServerConnection connection,
        bool initialized = false,
        int nextRequestId = 1)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        _initialized = initialized;
        _nextRequestId = nextRequestId - 1;
        _readLoop = ReadLoopAsync(_lifetime.Token);
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_initialized)
            return;

        await SendAsync<JsonElement>("initialize", new
        {
            clientInfo = new
            {
                name = "codex_token_bar",
                title = "Codex Token Bar",
                version = "0.1.0",
            },
        }, cancellationToken).ConfigureAwait(false);

        await WriteLineAsync(JsonSerializer.Serialize(new
        {
            method = "initialized",
            @params = new { },
        }), cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    public async Task<T> SendAsync<T>(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var id = Interlocked.Increment(ref _nextRequestId);
        var completion = new TaskCompletionSource<JsonElement>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new CodexProtocolException("无法注册 Codex 请求");

        using var registration = cancellationToken.Register(() =>
        {
            if (_pending.TryRemove(id, out var pending))
                pending.TrySetCanceled(cancellationToken);
        });

        try
        {
            await WriteLineAsync(JsonSerializer.Serialize(new
            {
                method,
                id,
                @params = parameters ?? new { },
            }), cancellationToken).ConfigureAwait(false);

            var result = await completion.Task.ConfigureAwait(false);
            if (typeof(T) == typeof(JsonElement))
                return (T)(object)result;

            return result.Deserialize<T>()
                ?? throw new CodexProtocolException("Codex 响应结果为空");
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _connection.Input.WriteLineAsync(line.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            await _connection.Input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        Exception? terminalError = null;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _connection.Output.ReadLineAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (line is null)
                {
                    var diagnostic = await _connection.ReadDiagnosticAsync(cancellationToken)
                        .ConfigureAwait(false);
                    throw new CodexProtocolException(string.IsNullOrWhiteSpace(diagnostic)
                        ? "Codex 连接已关闭"
                        : $"Codex 连接已关闭：{diagnostic}");
                }

                JsonDocument document;
                try
                {
                    document = JsonDocument.Parse(line);
                }
                catch (JsonException exception)
                {
                    throw new CodexProtocolException("Codex 返回了无效 JSON", exception);
                }

                using (document)
                {
                    var root = document.RootElement;
                    if (!root.TryGetProperty("id", out var idElement) ||
                        idElement.ValueKind != JsonValueKind.Number ||
                        !idElement.TryGetInt32(out var id))
                    {
                        continue;
                    }

                    if (!_pending.TryRemove(id, out var completion))
                        continue;

                    if (root.TryGetProperty("error", out var error))
                    {
                        var message = error.TryGetProperty("message", out var messageElement)
                            ? messageElement.GetString()
                            : null;
                        completion.TrySetException(new CodexProtocolException(
                            string.IsNullOrWhiteSpace(message) ? "Codex 请求失败" : message));
                    }
                    else if (root.TryGetProperty("result", out var result))
                    {
                        completion.TrySetResult(result.Clone());
                    }
                    else
                    {
                        completion.TrySetException(new CodexProtocolException("Codex 响应缺少结果"));
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            terminalError = exception is CodexProtocolException
                ? exception
                : new CodexProtocolException("读取 Codex 响应失败", exception);
        }
        finally
        {
            if (terminalError is not null)
                FailAllPending(terminalError);
        }
    }

    private void FailAllPending(Exception exception)
    {
        foreach (var item in _pending.ToArray())
        {
            if (_pending.TryRemove(item.Key, out var completion))
                completion.TrySetException(exception);
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _lifetime.Cancel();
        FailAllPending(new ObjectDisposedException(nameof(CodexAppServerClient)));
        await _connection.DisposeAsync().ConfigureAwait(false);
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        _writeLock.Dispose();
        _lifetime.Dispose();
    }
}
