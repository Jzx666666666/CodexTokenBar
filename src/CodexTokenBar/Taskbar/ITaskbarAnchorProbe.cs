using CodexTokenBar.Domain;

namespace CodexTokenBar.Taskbar;

public interface ITaskbarAnchorProbe
{
    bool TryGetAnchor(out TaskbarAnchor anchor);
}

public interface ITaskbarOverlayWindow
{
    event Action? LeftClick;
    event Action? DoubleClick;
    event Action? RightClickRequested;

    void SetAnchor(TaskbarAnchor anchor);
    void Show();
    void Hide();
    void ReassertTopmost();
    void Update(UsageState state);
    void Close();
}

public interface ITaskbarPositionLoop
{
    void Start(Action tick);
    void Stop();
}

public interface ITaskbarClickDelay
{
    void Schedule(TimeSpan delay, Action callback);
    void Cancel();
}
