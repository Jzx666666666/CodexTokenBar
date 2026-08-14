using CodexTokenBar.Domain;
using CodexTokenBar.Codex;
using CodexTokenBar.Monitoring;
using CodexTokenBar.Persistence;

namespace CodexTokenBar.Tests;

internal static class UsageMonitorTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("Monitor.StartsInStartingState", StartsInStartingState),
        new("Monitor.SuccessPublishesFreshState", SuccessPublishesFreshState),
        new("Monitor.RefreshingPreservesFreshQuota", RefreshingPreservesFreshQuota),
        new("Monitor.FailurePublishesStaleCachedQuota", FailurePublishesStaleCachedQuota),
        new("Monitor.ConcurrentRefreshIsSingleFlight", ConcurrentRefreshIsSingleFlight),
        new("Monitor.ResumeTriggersImmediateRead", ResumeTriggersImmediateRead),
        new("Monitor.RecoveryClearsError", RecoveryClearsError),
        new("Monitor.RetryScheduleAdvancesAndCaps", RetryScheduleAdvancesAndCaps),
        new("Monitor.ManualRefreshRestartsCompletedDelay", ManualRefreshRestartsCompletedDelay),
        new("Monitor.MissingCliPublishesSpecificChineseError", MissingCliPublishesSpecificChineseError),
    ];

    private static Task StartsInStartingState()
    {
        using var fixture = new MonitorFixture();
        Assert.Equal(UsageFreshness.Starting, fixture.Monitor.State.Freshness);
        Assert.Equal<QuotaView?>(null, fixture.Monitor.State.PrimaryQuota);
        return Task.CompletedTask;
    }

    private static async Task SuccessPublishesFreshState()
    {
        using var fixture = new MonitorFixture();
        fixture.Reader.EnqueueSnapshot(remaining: 47);

        await fixture.Monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageFreshness.Fresh, fixture.Monitor.State.Freshness);
        Assert.Equal(47, fixture.Monitor.State.PrimaryQuota?.RemainingPercent);
        Assert.Equal(fixture.Now, fixture.Monitor.State.LastSuccessfulUpdate);
        Assert.Equal<string?>(null, fixture.Monitor.State.ErrorMessage);
        Assert.Equal(1, fixture.Store.SaveSnapshotCount);
    }

    private static async Task RefreshingPreservesFreshQuota()
    {
        using var fixture = new MonitorFixture();
        fixture.Reader.EnqueueSnapshot(remaining: 47);
        await fixture.Monitor.RefreshAsync(CancellationToken.None);
        var gate = fixture.Reader.EnqueueGatedSnapshot(remaining: 46);

        var refresh = fixture.Monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageFreshness.Refreshing, fixture.Monitor.State.Freshness);
        Assert.Equal(47, fixture.Monitor.State.PrimaryQuota?.RemainingPercent);
        gate.SetResult();
        await refresh;
        Assert.Equal(46, fixture.Monitor.State.PrimaryQuota?.RemainingPercent);
    }

    private static async Task FailurePublishesStaleCachedQuota()
    {
        using var fixture = new MonitorFixture();
        fixture.Reader.EnqueueSnapshot(remaining: 47);
        fixture.Reader.EnqueueFailure(new IOException("broken stream"));
        await fixture.Monitor.RefreshAsync(CancellationToken.None);

        await fixture.Monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageFreshness.Stale, fixture.Monitor.State.Freshness);
        Assert.Equal(47, fixture.Monitor.State.PrimaryQuota?.RemainingPercent);
        Assert.Equal("Codex 连接或响应异常", fixture.Monitor.State.ErrorMessage);
        Assert.Equal(fixture.Now.AddSeconds(2), fixture.Monitor.State.NextRetryAt);
        Assert.Equal(1, fixture.Reader.ResetCount);
    }

    private static async Task ConcurrentRefreshIsSingleFlight()
    {
        using var fixture = new MonitorFixture();
        var gate = fixture.Reader.EnqueueGatedSnapshot(remaining: 47);

        var first = fixture.Monitor.RefreshAsync(CancellationToken.None);
        var second = fixture.Monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, fixture.Reader.ReadCount);
        gate.SetResult();
        await Task.WhenAll(first, second);
    }

    private static async Task ResumeTriggersImmediateRead()
    {
        using var fixture = new MonitorFixture();
        fixture.Reader.EnqueueSnapshot(remaining: 47);

        await fixture.Monitor.OnResumeAsync(CancellationToken.None);

        Assert.Equal(1, fixture.Reader.ReadCount);
        Assert.Equal(UsageFreshness.Fresh, fixture.Monitor.State.Freshness);
    }

    private static async Task RecoveryClearsError()
    {
        using var fixture = new MonitorFixture();
        fixture.Reader.EnqueueFailure(new IOException("broken"));
        fixture.Reader.EnqueueSnapshot(remaining: 44);
        await fixture.Monitor.RefreshAsync(CancellationToken.None);
        await fixture.Monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageFreshness.Fresh, fixture.Monitor.State.Freshness);
        Assert.Equal<string?>(null, fixture.Monitor.State.ErrorMessage);
        Assert.Equal<DateTimeOffset?>(null, fixture.Monitor.State.NextRetryAt);
    }

    private static async Task RetryScheduleAdvancesAndCaps()
    {
        using var fixture = new MonitorFixture();
        for (var index = 0; index < 6; index++)
        {
            fixture.Reader.EnqueueFailure(new IOException("broken"));
            await fixture.Monitor.RefreshAsync(CancellationToken.None);
            var expected = new[] { 2, 5, 15, 30, 60, 60 }[index];
            Assert.Equal(fixture.Now.AddSeconds(expected), fixture.Monitor.State.NextRetryAt);
        }
    }

    private static async Task ManualRefreshRestartsCompletedDelay()
    {
        var scheduler = new FakeDelayScheduler();
        using var fixture = new MonitorFixture(scheduler.DelayAsync);
        fixture.Reader.EnqueueSnapshot(remaining: 47);
        var nextRead = fixture.Reader.EnqueueGatedSnapshot(remaining: 46);
        await fixture.Monitor.StartAsync(CancellationToken.None);
        var firstDelay = await scheduler.NextAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), firstDelay.Duration);

        var manual = fixture.Monitor.RefreshAsync(CancellationToken.None);
        await Assert.ThrowsAsync<OperationCanceledException>(() => firstDelay.Completion);
        nextRead.SetResult();
        await manual;

        var restartedDelay = await scheduler.NextAsync();
        Assert.Equal(TimeSpan.FromSeconds(60), restartedDelay.Duration);
    }

    private static async Task MissingCliPublishesSpecificChineseError()
    {
        using var fixture = new MonitorFixture();
        fixture.Reader.EnqueueFailure(new CodexProcessException("未找到 Codex CLI，请先安装并登录 Codex"));

        await fixture.Monitor.RefreshAsync(CancellationToken.None);

        Assert.Equal(UsageFreshness.Stale, fixture.Monitor.State.Freshness);
        Assert.Equal("未找到 Codex CLI，请先安装并登录 Codex", fixture.Monitor.State.ErrorMessage);
    }

    private sealed class MonitorFixture : IDisposable
    {
        public DateTimeOffset Now { get; } = new(2026, 8, 12, 16, 42, 0, TimeSpan.FromHours(8));
        public FakeRateLimitReader Reader { get; } = new();
        public MemoryAppStateStore Store { get; } = new();
        public UsageMonitor Monitor { get; }

        public MonitorFixture(Func<TimeSpan, CancellationToken, Task>? delay = null)
        {
            Monitor = new UsageMonitor(Reader, Store, () => Now, delay);
        }

        public void Dispose() => Monitor.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

internal sealed class FakeDelayScheduler
{
    private readonly System.Threading.Channels.Channel<DelayCall> _calls =
        System.Threading.Channels.Channel.CreateUnbounded<DelayCall>();

    public Task DelayAsync(TimeSpan duration, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        completion.Task.ContinueWith(
            _ => registration.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        _calls.Writer.TryWrite(new DelayCall(duration, completion.Task));
        return completion.Task;
    }

    public async Task<DelayCall> NextAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        return await _calls.Reader.ReadAsync(timeout.Token);
    }

    public sealed record DelayCall(TimeSpan Duration, Task Completion);
}

internal sealed class FakeRateLimitReader : IRateLimitReader
{
    private readonly Queue<Func<CancellationToken, Task<RateLimitSnapshot>>> _reads = new();
    public int ReadCount { get; private set; }
    public int ResetCount { get; private set; }

    public void EnqueueSnapshot(int remaining) =>
        _reads.Enqueue(_ => Task.FromResult(CreateSnapshot(remaining)));

    public TaskCompletionSource EnqueueGatedSnapshot(int remaining)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _reads.Enqueue(async cancellationToken =>
        {
            await gate.Task.WaitAsync(cancellationToken);
            return CreateSnapshot(remaining);
        });
        return gate;
    }

    public void EnqueueFailure(Exception exception) =>
        _reads.Enqueue(_ => Task.FromException<RateLimitSnapshot>(exception));

    public Task<RateLimitSnapshot> ReadAsync(CancellationToken cancellationToken)
    {
        ReadCount++;
        if (_reads.Count == 0)
            throw new InvalidOperationException("No fake read is queued.");
        return _reads.Dequeue()(cancellationToken);
    }

    public Task ResetConnectionAsync(CancellationToken cancellationToken)
    {
        ResetCount++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static RateLimitSnapshot CreateSnapshot(int remaining) => new(
        new Dictionary<string, RateLimitPool>
        {
            ["codex"] = new("codex", null, new(100 - remaining, 10_080, 1_787_132_210), null),
        });
}

internal sealed class MemoryAppStateStore : IAppStateStore
{
    public AppSettings Settings { get; set; } = AppSettings.Default;
    public StoredQuotaSnapshot? Snapshot { get; set; }
    public int SaveSnapshotCount { get; private set; }

    public Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken) => Task.FromResult(Settings);
    public Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        Settings = settings;
        return Task.CompletedTask;
    }
    public Task<StoredQuotaSnapshot?> LoadLastSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(Snapshot);
    public Task SaveLastSnapshotAsync(StoredQuotaSnapshot snapshot, CancellationToken cancellationToken)
    {
        Snapshot = snapshot;
        SaveSnapshotCount++;
        return Task.CompletedTask;
    }
    public Task ClearAsync(CancellationToken cancellationToken)
    {
        Snapshot = null;
        Settings = AppSettings.Default;
        return Task.CompletedTask;
    }
}
