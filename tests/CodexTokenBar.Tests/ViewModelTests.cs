using CodexTokenBar.Domain;
using CodexTokenBar.UI;

namespace CodexTokenBar.Tests;

internal static class ViewModelTests
{
    private static readonly TimeZoneInfo ChinaTime = TimeZoneInfo.CreateCustomTimeZone(
        "China Test Time", TimeSpan.FromHours(8), "China Test Time", "China Test Time");

    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("ViewModel.FreshStateUsesChineseCopyAndLocalTime", FreshStateUsesChineseCopyAndLocalTime),
        new("ViewModel.StaleStateUsesRequiredCopy", StaleStateUsesRequiredCopy),
        new("ViewModel.UnavailableStateDoesNotPresentCachedValue", UnavailableStateDoesNotPresentCachedValue),
        new("ViewModel.OtherPoolsRemainSecondary", OtherPoolsRemainSecondary),
        new("ViewModel.RefreshingKeepsTrustedValue", RefreshingKeepsTrustedValue),
    ];

    private static Task FreshStateUsesChineseCopyAndLocalTime()
    {
        var vm = new SummaryViewModel(ChinaTime);
        vm.Apply(State(UsageFreshness.Fresh, 47));

        Assert.Equal("47%", vm.RemainingText);
        Assert.Equal("状态良好", vm.StatusText);
        Assert.Equal("下次重置：8月19日 16:30", vm.ResetText);
        Assert.Equal("最后更新：16:42", vm.LastUpdatedText);
        Assert.Equal("每 60 秒自动刷新", vm.RefreshCadenceText);
        Assert.Equal(false, vm.HasStaleQuota);
        return Task.CompletedTask;
    }

    private static Task StaleStateUsesRequiredCopy()
    {
        var vm = new SummaryViewModel(ChinaTime);
        vm.Apply(State(UsageFreshness.Stale, 47, "Codex 连接或响应异常"));

        Assert.Equal("数据已过期", vm.StatusText);
        Assert.Equal("最后成功更新：16:42", vm.LastUpdatedText);
        Assert.Equal(true, vm.HasStaleQuota);
        Assert.Equal("Codex 连接或响应异常", vm.ErrorText);
        Assert.Equal("47%", vm.RemainingText);
        return Task.CompletedTask;
    }

    private static Task UnavailableStateDoesNotPresentCachedValue()
    {
        var vm = new SummaryViewModel(ChinaTime);
        vm.Apply(UsageState.Starting);
        Assert.Equal("—", vm.RemainingText);
        Assert.Equal("正在读取额度", vm.StatusText);
        Assert.Equal(false, vm.HasStaleQuota);
        return Task.CompletedTask;
    }

    private static Task OtherPoolsRemainSecondary()
    {
        var vm = new SummaryViewModel(ChinaTime);
        var state = State(UsageFreshness.Fresh, 47) with
        {
            OtherQuotas = [new QuotaView("extra", "额外额度", 88, ResetAt)],
        };
        vm.Apply(state);

        Assert.Equal(1, vm.OtherQuotas.Count);
        Assert.Equal("额外额度", vm.OtherQuotas[0].DisplayName);
        Assert.Equal("88%", vm.OtherQuotas[0].RemainingText);
        Assert.Equal(true, vm.HasOtherQuotas);
        return Task.CompletedTask;
    }

    private static Task RefreshingKeepsTrustedValue()
    {
        var vm = new SummaryViewModel(ChinaTime);
        vm.Apply(State(UsageFreshness.Refreshing, 22));
        Assert.Equal("22%", vm.RemainingText);
        Assert.Equal("正在刷新", vm.StatusText);
        Assert.Equal(false, vm.HasStaleQuota);
        return Task.CompletedTask;
    }

    private static readonly DateTimeOffset ResetAt =
        new(2026, 8, 19, 8, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset UpdatedAt =
        new(2026, 8, 12, 8, 42, 0, TimeSpan.Zero);

    private static UsageState State(
        UsageFreshness freshness,
        int remaining,
        string? error = null) => new(
            freshness,
            new QuotaView("codex", "codex", remaining, ResetAt),
            Array.Empty<QuotaView>(),
            UpdatedAt,
            error,
            error is null ? null : UpdatedAt.AddSeconds(2));
}
