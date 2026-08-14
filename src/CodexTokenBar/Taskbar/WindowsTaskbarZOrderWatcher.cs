using System.Runtime.InteropServices;
using System.Text;

namespace CodexTokenBar.Taskbar;

public sealed class WindowsTaskbarZOrderWatcher : ITaskbarZOrderWatcher
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectReorder = 0x8004;
    private const uint WinEventSkipOwnProcess = 0x0002;

    private readonly object _gate = new();
    private readonly ITaskbarWinEventAdapter _adapter;
    private readonly TaskbarWinEventCallback _callback;
    private readonly List<IntPtr> _hooks = [];
    private IntPtr _taskbarWindow;
    private bool _started;
    private bool _disposed;

    public WindowsTaskbarZOrderWatcher()
        : this(new WindowsTaskbarWinEventAdapter())
    {
    }

    public WindowsTaskbarZOrderWatcher(ITaskbarWinEventAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _callback = OnWinEvent;
    }

    public event Action? RecoveryRequested;

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
                return;

            _started = true;
            _taskbarWindow = SafeFindTaskbarWindow();
            try
            {
                AddHook(EventSystemForeground);
                AddHook(EventObjectReorder);
            }
            catch
            {
                UnhookAll();
                _started = false;
            }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (!_started && _hooks.Count == 0)
                return;
            _started = false;
            _taskbarWindow = IntPtr.Zero;
            UnhookAll();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _started = false;
            _taskbarWindow = IntPtr.Zero;
            UnhookAll();
        }

        _adapter.Dispose();
    }

    private void AddHook(uint eventType)
    {
        var hook = _adapter.SetWinEventHook(
            eventType,
            eventType,
            _callback,
            WinEventSkipOwnProcess);
        if (hook == IntPtr.Zero)
            throw new InvalidOperationException("SetWinEventHook returned a null hook handle.");
        _hooks.Add(hook);
    }

    private void OnWinEvent(uint eventType, IntPtr window)
    {
        Action? handler = null;
        lock (_gate)
        {
            if (!_started || _disposed)
                return;

            if (eventType == EventSystemForeground)
            {
                handler = RecoveryRequested;
            }
            else if (eventType == EventObjectReorder)
            {
                var currentTaskbar = SafeFindTaskbarWindow();
                if (currentTaskbar != IntPtr.Zero)
                    _taskbarWindow = currentTaskbar;

                if (window != IntPtr.Zero &&
                    (window == _taskbarWindow || SafeIsTaskbarWindow(window)))
                {
                    handler = RecoveryRequested;
                }
            }
        }

        handler?.Invoke();
    }

    private IntPtr SafeFindTaskbarWindow()
    {
        try { return _adapter.FindTaskbarWindow(); }
        catch { return IntPtr.Zero; }
    }

    private bool SafeIsTaskbarWindow(IntPtr window)
    {
        try { return _adapter.IsTaskbarWindow(window); }
        catch { return false; }
    }

    private void UnhookAll()
    {
        foreach (var hook in _hooks)
        {
            try { _adapter.UnhookWinEvent(hook); } catch { }
        }
        _hooks.Clear();
    }
}

public sealed class WindowsTaskbarWinEventAdapter : ITaskbarWinEventAdapter
{
    private const uint WineventOutOfContext = 0x0000;
    private readonly object _gate = new();
    private readonly Dictionary<IntPtr, NativeWinEventDelegate> _callbacks = [];
    private bool _disposed;

    public IntPtr SetWinEventHook(
        uint eventMin,
        uint eventMax,
        TaskbarWinEventCallback callback,
        uint flags)
    {
        ArgumentNullException.ThrowIfNull(callback);
        ObjectDisposedException.ThrowIf(_disposed, this);

        NativeWinEventDelegate nativeCallback =
            (_, eventType, window, _, _, _, _) => callback(eventType, window);
        var hook = NativeSetWinEventHook(
            eventMin,
            eventMax,
            IntPtr.Zero,
            nativeCallback,
            0,
            0,
            WineventOutOfContext | flags);
        if (hook != IntPtr.Zero)
        {
            lock (_gate)
                _callbacks[hook] = nativeCallback;
        }
        return hook;
    }

    public bool UnhookWinEvent(IntPtr hook)
    {
        var result = NativeUnhookWinEvent(hook);
        lock (_gate)
            _callbacks.Remove(hook);
        return result;
    }

    public IntPtr FindTaskbarWindow() => FindWindow("Shell_TrayWnd", null);

    public bool IsTaskbarWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return false;
        var className = new StringBuilder(64);
        if (GetClassName(window, className, className.Capacity) == 0)
            return false;
        return className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }

    public void Dispose()
    {
        IntPtr[] hooks;
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            hooks = [.. _callbacks.Keys];
        }

        foreach (var hook in hooks)
            try { UnhookWinEvent(hook); } catch { }
    }

    private delegate void NativeWinEventDelegate(
        IntPtr hook,
        uint eventType,
        IntPtr window,
        int objectId,
        int childId,
        uint eventThread,
        uint eventTime);

    [DllImport("user32.dll", EntryPoint = "SetWinEventHook", SetLastError = true)]
    private static extern IntPtr NativeSetWinEventHook(
        uint eventMin,
        uint eventMax,
        IntPtr eventHookAssembly,
        NativeWinEventDelegate callback,
        uint processId,
        uint threadId,
        uint flags);

    [DllImport("user32.dll", EntryPoint = "UnhookWinEvent", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeUnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);
}
