using CodexTokenBar.Lifecycle;

namespace CodexTokenBar.Tests;

internal static class ApplicationLifecycleTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("Lifecycle.PrimaryStartupUsesRequiredOrder", PrimaryStartupUsesRequiredOrder),
        new("Lifecycle.SecondaryOnlyForwardsShowAndReleases", SecondaryOnlyForwardsShowAndReleases),
        new("Lifecycle.ShutdownIsOrderedAndIdempotent", ShutdownIsOrderedAndIdempotent),
        new("Lifecycle.ShutdownContinuesAfterCleanupFailure", ShutdownContinuesAfterCleanupFailure),
    ];

    private static async Task PrimaryStartupUsesRequiredOrder()
    {
        var log = new List<string>();
        var lifecycle = CreateLifecycle(log, acquire: true);

        var primary = await lifecycle.StartAsync(CancellationToken.None);

        Assert.Equal(true, primary);
        Assert.SequenceEqual(
            new[] { "acquire", "settings", "startup", "tray-gray", "overlay-start", "monitor-start" },
            log);
    }

    private static async Task SecondaryOnlyForwardsShowAndReleases()
    {
        var log = new List<string>();
        var lifecycle = CreateLifecycle(log, acquire: false);

        var primary = await lifecycle.StartAsync(CancellationToken.None);
        await lifecycle.ShutdownAsync();

        Assert.Equal(false, primary);
        Assert.SequenceEqual(new[] { "acquire", "show", "single-release" }, log);
    }

    private static async Task ShutdownIsOrderedAndIdempotent()
    {
        var log = new List<string>();
        var lifecycle = CreateLifecycle(log, acquire: true);
        await lifecycle.StartAsync(CancellationToken.None);
        log.Clear();

        await Task.WhenAll(lifecycle.ShutdownAsync(), lifecycle.ShutdownAsync());

        Assert.SequenceEqual(
            new[]
            {
                "overlay-stop",
                "monitor-stop",
                "resume-unsubscribe",
                "tray-dispose",
                "window-close",
                "reader-dispose",
                "single-release",
            },
            log);
    }

    private static async Task ShutdownContinuesAfterCleanupFailure()
    {
        var log = new List<string>();
        var actions = CreateActions(log, acquire: true);
        actions = actions with
        {
            StopMonitorAsync = () =>
            {
                log.Add("monitor-stop");
                return Task.FromException(new IOException("stop failed"));
            },
        };
        var lifecycle = new ApplicationLifecycle(actions);
        await lifecycle.StartAsync(CancellationToken.None);
        log.Clear();

        await Assert.ThrowsAsync<AggregateException>(() => lifecycle.ShutdownAsync());

        Assert.SequenceEqual(
            new[]
            {
                "overlay-stop", "monitor-stop", "resume-unsubscribe", "tray-dispose",
                "window-close", "reader-dispose", "single-release",
            },
            log);
    }

    private static ApplicationLifecycle CreateLifecycle(List<string> log, bool acquire) =>
        new(CreateActions(log, acquire));

    private static LifecycleActions CreateActions(List<string> log, bool acquire) =>
        new()
        {
            AcquireSingleInstanceAsync = _ => RecordResult(log, "acquire", acquire),
            NotifyExistingInstanceAsync = _ => Record(log, "show"),
            LoadSettingsAsync = _ => Record(log, "settings"),
            ApplyStartupAsync = _ => Record(log, "startup"),
            CreateGrayTray = () => log.Add("tray-gray"),
            StartOverlay = () => log.Add("overlay-start"),
            StopOverlay = () => log.Add("overlay-stop"),
            StartMonitorAsync = _ => Record(log, "monitor-start"),
            StopMonitorAsync = () => Record(log, "monitor-stop"),
            UnsubscribeResume = () => log.Add("resume-unsubscribe"),
            DisposeTray = () => log.Add("tray-dispose"),
            CloseWindow = () => log.Add("window-close"),
            DisposeReaderAsync = () => RecordValue(log, "reader-dispose"),
            ReleaseSingleInstanceAsync = () => RecordValue(log, "single-release"),
        };

    private static Task Record(List<string> log, string value)
    {
        log.Add(value);
        return Task.CompletedTask;
    }

    private static Task<bool> RecordResult(List<string> log, string value, bool result)
    {
        log.Add(value);
        return Task.FromResult(result);
    }

    private static ValueTask RecordValue(List<string> log, string value)
    {
        log.Add(value);
        return ValueTask.CompletedTask;
    }
}
