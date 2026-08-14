namespace CodexTokenBar.Domain;

public static class QuotaSelector
{
    public const int WeeklyWindowMinutes = 10_080;

    public static (QuotaView Primary, IReadOnlyList<QuotaView> Others) Select(
        RateLimitSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.Pools.TryGetValue("codex", out var codexPool))
        {
            codexPool = snapshot.Pools.Values.FirstOrDefault(pool =>
                string.Equals(pool.LimitId, "codex", StringComparison.Ordinal));
        }

        if (codexPool is null)
            throw new QuotaSelectionException("未找到常规 Codex 额度池");

        var weeklyWindow = FindWeeklyWindow(codexPool)
            ?? throw new QuotaSelectionException("未找到 Codex 周额度窗口");

        var otherQuotas = snapshot.Pools.Values
            .Where(pool => !ReferenceEquals(pool, codexPool))
            .Select(pool => (Pool: pool, Window: FindWeeklyWindow(pool)))
            .Where(item => item.Window is not null)
            .Select(item => ToView(item.Pool, item.Window!))
            .ToArray();

        return (ToView(codexPool, weeklyWindow), otherQuotas);
    }

    private static RateLimitWindow? FindWeeklyWindow(RateLimitPool pool) =>
        new[] { pool.Primary, pool.Secondary }
            .FirstOrDefault(window => window?.WindowDurationMins == WeeklyWindowMinutes);

    private static QuotaView ToView(RateLimitPool pool, RateLimitWindow window)
    {
        var displayName = string.IsNullOrWhiteSpace(pool.LimitName)
            ? pool.LimitId
            : pool.LimitName;
        DateTimeOffset? resetsAt = window.ResetsAt is long timestamp
            ? DateTimeOffset.FromUnixTimeSeconds(timestamp)
            : null;

        return new QuotaView(
            pool.LimitId,
            displayName,
            Math.Clamp(100 - window.UsedPercent, 0, 100),
            resetsAt);
    }
}
