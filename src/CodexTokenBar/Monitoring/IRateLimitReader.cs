using CodexTokenBar.Domain;

namespace CodexTokenBar.Monitoring;

public interface IRateLimitReader : IAsyncDisposable
{
    Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken);
    Task ResetConnectionAsync(CancellationToken cancellationToken);
}
