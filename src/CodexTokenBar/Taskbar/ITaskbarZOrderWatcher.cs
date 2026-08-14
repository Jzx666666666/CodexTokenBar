namespace CodexTokenBar.Taskbar;

public interface ITaskbarZOrderWatcher : IDisposable
{
    event Action? RecoveryRequested;

    void Start();
    void Stop();
}

public delegate void TaskbarWinEventCallback(uint eventType, IntPtr window);

public interface ITaskbarWinEventAdapter : IDisposable
{
    IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        TaskbarWinEventCallback callback,
        uint flags);

    bool UnhookWinEvent(IntPtr hook);
    IntPtr FindTaskbarWindow();
    bool IsTaskbarWindow(IntPtr window);
}

public interface ITaskbarUiDispatcher
{
    void Post(Action action);
}

public interface ITaskbarRecoveryDelay
{
    void Schedule(TimeSpan delay, Action callback);
    void Cancel();
}
