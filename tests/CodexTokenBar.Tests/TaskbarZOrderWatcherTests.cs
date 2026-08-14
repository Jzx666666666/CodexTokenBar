using CodexTokenBar.Domain;
using CodexTokenBar.Taskbar;

namespace CodexTokenBar.Tests;

internal static class TaskbarZOrderWatcherTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("TaskbarZOrder.WatcherInstallsForegroundAndReorderHooksOnce", WatcherInstallsForegroundAndReorderHooksOnce),
        new("TaskbarZOrder.WatcherFiltersReorderAndRefreshesExplorerHandle", WatcherFiltersReorderAndRefreshesExplorerHandle),
        new("TaskbarZOrder.WatcherStopAndDisposeAreIdempotent", WatcherStopAndDisposeAreIdempotent),
        new("TaskbarZOrder.WatcherHookFailureDoesNotEscape", WatcherHookFailureDoesNotEscape),
        new("TaskbarOverlay.ZOrderRecoveryIsDispatchedAndTrailingWorkCoalesces", ZOrderRecoveryIsDispatchedAndTrailingWorkCoalesces),
        new("TaskbarOverlay.ZOrderRecoveryNeverShowsUnavailableOverlay", ZOrderRecoveryNeverShowsUnavailableOverlay),
        new("TaskbarOverlay.StopCancelsPendingZOrderRecovery", StopCancelsPendingZOrderRecovery),
        new("TaskbarOverlay.WatcherFailureDoesNotStopGeometryWatchdog", WatcherFailureDoesNotStopGeometryWatchdog),
    ];

    private static Task WatcherInstallsForegroundAndReorderHooksOnce()
    {
        var adapter = new FakeWinEventAdapter(new IntPtr(101));
        using var watcher = new WindowsTaskbarZOrderWatcher(adapter);
        var recoveryCount = 0;
        watcher.RecoveryRequested += () => recoveryCount++;

        watcher.Start();
        watcher.Start();

        Assert.Equal(2, adapter.HookRequests.Count);
        Assert.Equal(0x0003u, adapter.HookRequests[0].EventMin);
        Assert.Equal(0x0003u, adapter.HookRequests[0].EventMax);
        Assert.Equal(0x8004u, adapter.HookRequests[1].EventMin);
        Assert.Equal(0x8004u, adapter.HookRequests[1].EventMax);
        Assert.Equal(0x0002u, adapter.HookRequests[0].Flags);
        Assert.Equal(0x0002u, adapter.HookRequests[1].Flags);

        adapter.Raise(0x0003u, IntPtr.Zero);
        adapter.Raise(0x8004u, new IntPtr(101));

        Assert.Equal(2, recoveryCount);
        return Task.CompletedTask;
    }

    private static Task WatcherFiltersReorderAndRefreshesExplorerHandle()
    {
        var originalTaskbar = new IntPtr(201);
        var replacementTaskbar = new IntPtr(202);
        var unrelatedWindow = new IntPtr(203);
        var adapter = new FakeWinEventAdapter(originalTaskbar);
        adapter.TaskbarWindows.Add(originalTaskbar);
        using var watcher = new WindowsTaskbarZOrderWatcher(adapter);
        var recoveryCount = 0;
        watcher.RecoveryRequested += () => recoveryCount++;
        watcher.Start();

        adapter.Raise(0x8004u, unrelatedWindow);
        Assert.Equal(0, recoveryCount);

        adapter.Raise(0x8004u, originalTaskbar);
        Assert.Equal(1, recoveryCount);

        adapter.TaskbarWindows.Remove(originalTaskbar);
        adapter.TaskbarWindows.Add(replacementTaskbar);
        adapter.CurrentTaskbarWindow = replacementTaskbar;
        adapter.Raise(0x8004u, replacementTaskbar);
        Assert.Equal(2, recoveryCount);

        adapter.Raise(0x8004u, originalTaskbar);
        Assert.Equal(2, recoveryCount);

        adapter.Raise(0x0003u, unrelatedWindow);
        Assert.Equal(3, recoveryCount);
        return Task.CompletedTask;
    }

    private static Task WatcherStopAndDisposeAreIdempotent()
    {
        var adapter = new FakeWinEventAdapter(new IntPtr(301));
        var watcher = new WindowsTaskbarZOrderWatcher(adapter);
        var recoveryCount = 0;
        watcher.RecoveryRequested += () => recoveryCount++;

        watcher.Start();
        watcher.Stop();
        watcher.Stop();
        watcher.Dispose();
        watcher.Dispose();

        Assert.Equal(2, adapter.UnhookedHooks.Count);
        Assert.Equal(1, adapter.DisposeCount);
        adapter.Raise(0x0003u, IntPtr.Zero);
        Assert.Equal(0, recoveryCount);
        return Task.CompletedTask;
    }

    private static Task WatcherHookFailureDoesNotEscape()
    {
        var adapter = new FakeWinEventAdapter(new IntPtr(401)) { ThrowOnSetHook = true };
        using var watcher = new WindowsTaskbarZOrderWatcher(adapter);

        watcher.Start();
        watcher.Stop();

        Assert.Equal(0, adapter.ActiveHookCount);
        return Task.CompletedTask;
    }

    private static Task ZOrderRecoveryIsDispatchedAndTrailingWorkCoalesces()
    {
        var watcher = new FakeZOrderWatcher();
        var dispatcher = new QueuedUiDispatcher();
        var recoveryDelay = new ManualRecoveryDelay();
        var window = new RecordingOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = CreateCoordinator(
            new FakeAnchorProbe(Anchor()),
            window,
            loop,
            watcher,
            dispatcher,
            recoveryDelay);

        coordinator.Start();
        watcher.RaiseRecovery();
        watcher.RaiseRecovery();
        watcher.RaiseRecovery();

        Assert.Equal(0, window.ReassertTopmostCount);
        Assert.Equal(1, dispatcher.PostCount);

        dispatcher.FlushNext();

        Assert.Equal(1, window.ReassertTopmostCount);
        Assert.Equal(1, recoveryDelay.ScheduleCount);
        Assert.Equal(TimeSpan.FromMilliseconds(50), recoveryDelay.LastDelay);

        watcher.RaiseRecovery();
        watcher.RaiseRecovery();
        Assert.Equal(1, dispatcher.PostCount);
        recoveryDelay.Fire();
        Assert.Equal(2, window.ReassertTopmostCount);

        watcher.RaiseRecovery();
        dispatcher.FlushNext();
        recoveryDelay.Fire();
        Assert.Equal(4, window.ReassertTopmostCount);
        Assert.Equal(2, dispatcher.PostCount);
        Assert.Equal(2, recoveryDelay.ScheduleCount);
        return Task.CompletedTask;
    }

    private static Task ZOrderRecoveryNeverShowsUnavailableOverlay()
    {
        var watcher = new FakeZOrderWatcher();
        var dispatcher = new QueuedUiDispatcher();
        var recoveryDelay = new ManualRecoveryDelay();
        var window = new RecordingOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = CreateCoordinator(
            new FakeAnchorProbe(null),
            window,
            loop,
            watcher,
            dispatcher,
            recoveryDelay);

        coordinator.Start();
        watcher.RaiseRecovery();
        dispatcher.FlushAll();
        recoveryDelay.Fire();

        Assert.Equal(false, coordinator.IsOverlayAvailable);
        Assert.Equal(0, window.ShowCount);
        Assert.Equal(0, window.ReassertTopmostCount);
        Assert.Equal(0, dispatcher.PostCount);
        return Task.CompletedTask;
    }

    private static Task StopCancelsPendingZOrderRecovery()
    {
        var watcher = new FakeZOrderWatcher();
        var dispatcher = new QueuedUiDispatcher();
        var recoveryDelay = new ManualRecoveryDelay();
        var window = new RecordingOverlayWindow();
        var loop = new ManualPositionLoop();
        var coordinator = CreateCoordinator(
            new FakeAnchorProbe(Anchor()),
            window,
            loop,
            watcher,
            dispatcher,
            recoveryDelay);

        coordinator.Start();
        watcher.RaiseRecovery();
        dispatcher.FlushNext();
        coordinator.Stop();

        watcher.RaiseRecovery();
        dispatcher.FlushAll();
        recoveryDelay.Fire();

        Assert.Equal(1, watcher.StopCount);
        Assert.Equal(1, recoveryDelay.CancelCount);
        Assert.Equal(1, window.ReassertTopmostCount);
        coordinator.Dispose();
        return Task.CompletedTask;
    }

    private static Task WatcherFailureDoesNotStopGeometryWatchdog()
    {
        var watcher = new FakeZOrderWatcher { ThrowOnStart = true };
        var dispatcher = new QueuedUiDispatcher();
        var recoveryDelay = new ManualRecoveryDelay();
        var window = new RecordingOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = CreateCoordinator(
            new FakeAnchorProbe(Anchor()),
            window,
            loop,
            watcher,
            dispatcher,
            recoveryDelay);

        coordinator.Start();

        Assert.Equal(1, loop.StartCount);
        Assert.Equal(1, window.ShowCount);
        return Task.CompletedTask;
    }

    private static TaskbarOverlayCoordinator CreateCoordinator(
        ITaskbarAnchorProbe probe,
        ITaskbarOverlayWindow window,
        ITaskbarPositionLoop loop,
        ITaskbarZOrderWatcher watcher,
        ITaskbarUiDispatcher dispatcher,
        ITaskbarRecoveryDelay recoveryDelay) =>
        new(
            probe,
            window,
            loop,
            new NoopClickDelay(),
            TimeSpan.FromMilliseconds(250),
            watcher,
            dispatcher,
            recoveryDelay);

    private static TaskbarAnchor Anchor() => new(1756, 1032, 64, 48);

    private sealed class FakeWinEventAdapter(IntPtr currentTaskbarWindow) : ITaskbarWinEventAdapter
    {
        private readonly Dictionary<IntPtr, HookRequest> _hooks = [];
        private int _nextHook = 1;

        public List<HookRequest> HookRequests { get; } = [];
        public List<IntPtr> UnhookedHooks { get; } = [];
        public HashSet<IntPtr> TaskbarWindows { get; } = [];
        public IntPtr CurrentTaskbarWindow { get; set; } = currentTaskbarWindow;
        public bool ThrowOnSetHook { get; init; }
        public int DisposeCount { get; private set; }
        public int ActiveHookCount => _hooks.Count;

        public IntPtr SetWinEventHook(
            uint eventMin,
            uint eventMax,
            TaskbarWinEventCallback callback,
            uint flags)
        {
            if (ThrowOnSetHook)
                throw new InvalidOperationException("synthetic hook installation failure");

            var hook = new IntPtr(_nextHook++);
            var request = new HookRequest(eventMin, eventMax, callback, flags, hook);
            HookRequests.Add(request);
            _hooks.Add(hook, request);
            return hook;
        }

        public bool UnhookWinEvent(IntPtr hook)
        {
            UnhookedHooks.Add(hook);
            return _hooks.Remove(hook);
        }

        public IntPtr FindTaskbarWindow() => CurrentTaskbarWindow;

        public bool IsTaskbarWindow(IntPtr window) => TaskbarWindows.Contains(window);

        public void Dispose() => DisposeCount++;

        public void Raise(uint eventType, IntPtr window)
        {
            foreach (var request in _hooks.Values.ToArray())
            {
                if (eventType >= request.EventMin && eventType <= request.EventMax)
                    request.Callback(eventType, window);
            }
        }
    }

    private readonly record struct HookRequest(
        uint EventMin,
        uint EventMax,
        TaskbarWinEventCallback Callback,
        uint Flags,
        IntPtr Hook);

    private sealed class FakeZOrderWatcher : ITaskbarZOrderWatcher
    {
        public event Action? RecoveryRequested;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool ThrowOnStart { get; init; }

        public void Start()
        {
            StartCount++;
            if (ThrowOnStart)
                throw new InvalidOperationException("synthetic watcher start failure");
        }

        public void Stop() => StopCount++;

        public void Dispose() => DisposeCount++;

        public void RaiseRecovery() => RecoveryRequested?.Invoke();
    }

    private sealed class QueuedUiDispatcher : ITaskbarUiDispatcher
    {
        private readonly Queue<Action> _pending = [];

        public int PostCount { get; private set; }

        public void Post(Action action)
        {
            PostCount++;
            _pending.Enqueue(action);
        }

        public void FlushNext()
        {
            if (_pending.Count == 0)
                throw new InvalidOperationException("No UI action is pending.");
            _pending.Dequeue()();
        }

        public void FlushAll()
        {
            while (_pending.Count > 0)
                FlushNext();
        }
    }

    private sealed class ManualRecoveryDelay : ITaskbarRecoveryDelay
    {
        private Action? _callback;

        public int ScheduleCount { get; private set; }
        public int CancelCount { get; private set; }
        public TimeSpan LastDelay { get; private set; }

        public void Schedule(TimeSpan delay, Action callback)
        {
            ScheduleCount++;
            LastDelay = delay;
            _callback = callback;
        }

        public void Cancel()
        {
            CancelCount++;
            _callback = null;
        }

        public void Fire()
        {
            var callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }

    private sealed class FakeAnchorProbe(TaskbarAnchor? result) : ITaskbarAnchorProbe
    {
        public bool TryGetAnchor(out TaskbarAnchor anchor)
        {
            if (result is { } value)
            {
                anchor = value;
                return true;
            }

            anchor = null!;
            return false;
        }
    }

    private sealed class RecordingOverlayWindow : ITaskbarOverlayWindow
    {
        public event Action? LeftClick { add { } remove { } }
        public event Action? DoubleClick { add { } remove { } }
        public event Action? RightClickRequested { add { } remove { } }

        public int ShowCount { get; private set; }
        public int ReassertTopmostCount { get; private set; }

        public void SetAnchor(TaskbarAnchor anchor) { }
        public void Show() => ShowCount++;
        public void Hide() { }
        public void ReassertTopmost() => ReassertTopmostCount++;
        public void Update(UsageState state) { }
        public void Close() { }
    }

    private sealed class ManualPositionLoop : ITaskbarPositionLoop
    {
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public void Start(Action tick) => StartCount++;
        public void Stop() => StopCount++;
    }

    private sealed class NoopClickDelay : ITaskbarClickDelay
    {
        public void Schedule(TimeSpan delay, Action callback) { }
        public void Cancel() { }
    }
}
