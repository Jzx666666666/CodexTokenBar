using CodexTokenBar.Domain;
using CodexTokenBar.Monitoring;

namespace CodexTokenBar.Codex;

public sealed class CodexRateLimitReader : IRateLimitReader
{
    private readonly Func<CancellationToken, Task<IAppServerConnection>> _connectionFactory;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private CodexAppServerClient? _client;
    private int _disposed;

    public CodexRateLimitReader(
        Func<CancellationToken, Task<IAppServerConnection>> connectionFactory)
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
    }

    public async Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        var client = await GetClientAsync(cancellationToken).ConfigureAwait(false);
        var response = await client.SendAsync<GetRateLimitsResponse>(
            "account/rateLimits/read",
            new { },
            cancellationToken).ConfigureAwait(false);
        return Map(response);
    }

    public async Task ResetConnectionAsync(CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var client = _client;
            _client = null;
            if (client is not null)
                await client.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<CodexAppServerClient> GetClientAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_client is not null)
                return _client;

            var connection = await _connectionFactory(cancellationToken).ConfigureAwait(false);
            var client = new CodexAppServerClient(connection);
            try
            {
                await client.InitializeAsync(cancellationToken).ConfigureAwait(false);
                _client = client;
                return client;
            }
            catch
            {
                await client.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private static RateLimitSnapshot Map(GetRateLimitsResponse response)
    {
        if (response is null)
            throw new CodexProtocolException("Codex 额度响应为空");

        IEnumerable<KeyValuePair<string, RateLimitPoolDto>> source;
        if (response.RateLimitsByLimitId is { Count: > 0 } pools)
        {
            source = pools;
        }
        else if (response.RateLimits is { } legacy)
        {
            source = new[]
            {
                new KeyValuePair<string, RateLimitPoolDto>(legacy.LimitId ?? "codex", legacy),
            };
        }
        else
        {
            throw new CodexProtocolException("Codex 额度响应缺少额度池");
        }

        var mapped = new Dictionary<string, RateLimitPool>(StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (string.IsNullOrWhiteSpace(value.LimitId))
                throw new CodexProtocolException("Codex 额度池缺少 limitId");
            if (value.Primary is null && value.Secondary is null)
                throw new CodexProtocolException("Codex 额度池缺少额度窗口");

            mapped[key] = new RateLimitPool(
                value.LimitId,
                value.LimitName,
                MapWindow(value.Primary),
                MapWindow(value.Secondary));
        }

        return new RateLimitSnapshot(mapped);
    }

    private static RateLimitWindow? MapWindow(RateLimitWindowDto? window)
    {
        if (window is null)
            return null;
        if (window.UsedPercent is null)
            throw new CodexProtocolException("Codex 额度窗口缺少 usedPercent");
        return new RateLimitWindow(
            window.UsedPercent.Value,
            window.WindowDurationMins,
            window.ResetsAt);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await ResetConnectionAsync(CancellationToken.None).ConfigureAwait(false);
        _connectionLock.Dispose();
    }
}
