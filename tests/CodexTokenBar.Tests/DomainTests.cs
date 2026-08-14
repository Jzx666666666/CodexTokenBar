using CodexTokenBar.Domain;

namespace CodexTokenBar.Tests;

internal static class DomainTests
{
    private const long ResetTimestamp = 1_787_132_210;

    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("Domain.SelectsCodexWeeklyWindow", SelectsCodexWeeklyWindow),
        new("Domain.SelectsWeeklyWindowFromSecondary", SelectsWeeklyWindowFromSecondary),
        new("Domain.RejectsMissingCodexWeeklyWindow", RejectsMissingCodexWeeklyWindow),
        new("Domain.FallsBackToPoolLimitId", FallsBackToPoolLimitId),
        new("Domain.ClampsRemainingPercent", ClampsRemainingPercent),
        new("Domain.ConvertsUnixResetTimestamp", ConvertsUnixResetTimestamp),
        new("Domain.UsesExactColorBoundaries", UsesExactColorBoundaries),
        new("Domain.UsesRequiredRetrySequence", UsesRequiredRetrySequence),
        new("Domain.IncludesOnlyOtherWeeklyPools", IncludesOnlyOtherWeeklyPools),
    ];

    private static Task SelectsCodexWeeklyWindow()
    {
        var result = QuotaSelector.Select(
            Snapshot(("codex", Pool("codex", "常规 Codex", Window(1), null))));

        Assert.Equal("codex", result.Primary.LimitId);
        Assert.Equal("常规 Codex", result.Primary.DisplayName);
        Assert.Equal(99, result.Primary.RemainingPercent);
        return Task.CompletedTask;
    }

    private static Task SelectsWeeklyWindowFromSecondary()
    {
        var result = QuotaSelector.Select(Snapshot(("codex", Pool("codex", null,
            new RateLimitWindow(50, 300, ResetTimestamp), Window(8)))));

        Assert.Equal(92, result.Primary.RemainingPercent);
        Assert.Equal("codex", result.Primary.DisplayName);
        return Task.CompletedTask;
    }

    private static Task RejectsMissingCodexWeeklyWindow()
    {
        var snapshot = Snapshot(("codex", Pool("codex", null,
            new RateLimitWindow(5, 300, ResetTimestamp), null)));

        var exception = Assert.Throws<QuotaSelectionException>(() => QuotaSelector.Select(snapshot));

        Assert.Equal("未找到 Codex 周额度窗口", exception.Message);
        return Task.CompletedTask;
    }

    private static Task FallsBackToPoolLimitId()
    {
        var result = QuotaSelector.Select(
            Snapshot(("legacy-key", Pool("codex", null, Window(4), null))));

        Assert.Equal(96, result.Primary.RemainingPercent);
        return Task.CompletedTask;
    }

    private static Task ClampsRemainingPercent()
    {
        var belowZeroUsed = QuotaSelector.Select(
            Snapshot(("codex", Pool("codex", null, Window(-20), null))));
        var aboveHundredUsed = QuotaSelector.Select(
            Snapshot(("codex", Pool("codex", null, Window(140), null))));

        Assert.Equal(100, belowZeroUsed.Primary.RemainingPercent);
        Assert.Equal(0, aboveHundredUsed.Primary.RemainingPercent);
        return Task.CompletedTask;
    }

    private static Task ConvertsUnixResetTimestamp()
    {
        var result = QuotaSelector.Select(
            Snapshot(("codex", Pool("codex", null, Window(1), null))));

        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(ResetTimestamp), result.Primary.ResetsAt);
        return Task.CompletedTask;
    }

    private static Task UsesExactColorBoundaries()
    {
        Assert.Equal(QuotaTone.Green, QuotaPresentation.GetTone(31));
        Assert.Equal(QuotaTone.Yellow, QuotaPresentation.GetTone(30));
        Assert.Equal(QuotaTone.Yellow, QuotaPresentation.GetTone(10));
        Assert.Equal(QuotaTone.Red, QuotaPresentation.GetTone(9));
        Assert.Equal(QuotaTone.Gray, QuotaPresentation.GetTone(null));
        return Task.CompletedTask;
    }

    private static Task UsesRequiredRetrySequence()
    {
        var delays = Enumerable.Range(0, 6)
            .Select(attempt => RetrySchedule.GetDelay(attempt).TotalSeconds);

        Assert.SequenceEqual(new[] { 2d, 5d, 15d, 30d, 60d, 60d }, delays);
        return Task.CompletedTask;
    }

    private static Task IncludesOnlyOtherWeeklyPools()
    {
        var result = QuotaSelector.Select(Snapshot(
            ("codex", Pool("codex", "常规 Codex", Window(1), null)),
            ("spark", Pool("spark", "Codex Spark", null, Window(58))),
            ("short", Pool("short", "短窗口", new RateLimitWindow(20, 300, ResetTimestamp), null)),
            ("fallback", Pool("fallback", "", Window(70), null))));

        Assert.Equal(2, result.Others.Count);
        Assert.Equal("Codex Spark", result.Others[0].DisplayName);
        Assert.Equal("fallback", result.Others[1].DisplayName);
        return Task.CompletedTask;
    }

    private static RateLimitWindow Window(int usedPercent) =>
        new(usedPercent, 10_080, ResetTimestamp);

    private static RateLimitPool Pool(
        string limitId,
        string? limitName,
        RateLimitWindow? primary,
        RateLimitWindow? secondary) =>
        new(limitId, limitName, primary, secondary);

    private static RateLimitSnapshot Snapshot(params (string Key, RateLimitPool Pool)[] pools) =>
        new(pools.ToDictionary(item => item.Key, item => item.Pool, StringComparer.Ordinal));
}
