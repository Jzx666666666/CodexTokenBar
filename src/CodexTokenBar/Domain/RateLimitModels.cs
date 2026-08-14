namespace CodexTokenBar.Domain;

public sealed record RateLimitWindow(
    int UsedPercent,
    int? WindowDurationMins,
    long? ResetsAt);

public sealed record RateLimitPool(
    string LimitId,
    string? LimitName,
    RateLimitWindow? Primary,
    RateLimitWindow? Secondary);

public sealed record RateLimitSnapshot(
    IReadOnlyDictionary<string, RateLimitPool> Pools);

public sealed record QuotaView(
    string LimitId,
    string DisplayName,
    int RemainingPercent,
    DateTimeOffset? ResetsAt);

public sealed class QuotaSelectionException : Exception
{
    public QuotaSelectionException(string message) : base(message)
    {
    }
}
