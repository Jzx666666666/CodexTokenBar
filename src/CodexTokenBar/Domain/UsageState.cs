namespace CodexTokenBar.Domain;

public enum UsageFreshness
{
    Starting,
    Fresh,
    Refreshing,
    Stale,
}

public sealed record UsageState(
    UsageFreshness Freshness,
    QuotaView? PrimaryQuota,
    IReadOnlyList<QuotaView> OtherQuotas,
    DateTimeOffset? LastSuccessfulUpdate,
    string? ErrorMessage,
    DateTimeOffset? NextRetryAt)
{
    public static UsageState Starting { get; } = new(
        UsageFreshness.Starting,
        null,
        Array.Empty<QuotaView>(),
        null,
        null,
        null);
}
