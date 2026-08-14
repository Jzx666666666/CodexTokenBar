namespace CodexTokenBar.Lifecycle;

public sealed record LifecycleActions
{
    public required Func<CancellationToken, Task<bool>> AcquireSingleInstanceAsync { get; init; }
    public required Func<CancellationToken, Task> NotifyExistingInstanceAsync { get; init; }
    public required Func<CancellationToken, Task> LoadSettingsAsync { get; init; }
    public required Func<CancellationToken, Task> ApplyStartupAsync { get; init; }
    public required Action CreateGrayTray { get; init; }
    public Action? StartOverlay { get; init; }
    public Action? StopOverlay { get; init; }
    public required Func<CancellationToken, Task> StartMonitorAsync { get; init; }
    public required Func<Task> StopMonitorAsync { get; init; }
    public required Action UnsubscribeResume { get; init; }
    public required Action DisposeTray { get; init; }
    public required Action CloseWindow { get; init; }
    public required Func<ValueTask> DisposeReaderAsync { get; init; }
    public required Func<ValueTask> ReleaseSingleInstanceAsync { get; init; }
}

public sealed class ApplicationLifecycle
{
    private readonly LifecycleActions _actions;
    private readonly object _shutdownGate = new();
    private Task? _shutdownTask;
    private bool _primary;

    public ApplicationLifecycle(LifecycleActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public async Task<bool> StartAsync(CancellationToken cancellationToken)
    {
        _primary = await _actions.AcquireSingleInstanceAsync(cancellationToken);
        if (!_primary)
        {
            await _actions.NotifyExistingInstanceAsync(cancellationToken);
            return false;
        }

        await _actions.LoadSettingsAsync(cancellationToken);
        await _actions.ApplyStartupAsync(cancellationToken);
        _actions.CreateGrayTray();
        _actions.StartOverlay?.Invoke();
        await _actions.StartMonitorAsync(cancellationToken);
        return true;
    }

    public Task ShutdownAsync()
    {
        lock (_shutdownGate)
            return _shutdownTask ??= ShutdownCoreAsync();
    }

    private async Task ShutdownCoreAsync()
    {
        var failures = new List<Exception>();
        if (_primary)
        {
            Capture(_actions.StopOverlay ?? (() => { }), failures);
            await CaptureAsync(_actions.StopMonitorAsync, failures);
            Capture(_actions.UnsubscribeResume, failures);
            Capture(_actions.DisposeTray, failures);
            Capture(_actions.CloseWindow, failures);
            await CaptureAsync(() => _actions.DisposeReaderAsync().AsTask(), failures);
        }
        await CaptureAsync(() => _actions.ReleaseSingleInstanceAsync().AsTask(), failures);
        if (failures.Count > 0)
            throw new AggregateException("应用清理期间发生错误", failures);
    }

    private static void Capture(Action action, ICollection<Exception> failures)
    {
        try { action(); }
        catch (Exception exception) { failures.Add(exception); }
    }

    private static async Task CaptureAsync(Func<Task> action, ICollection<Exception> failures)
    {
        try { await action(); }
        catch (Exception exception) { failures.Add(exception); }
    }
}
