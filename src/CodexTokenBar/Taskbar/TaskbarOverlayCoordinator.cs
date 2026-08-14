using CodexTokenBar.Domain;
using Microsoft.Win32;
using System.Windows.Threading;

namespace CodexTokenBar.Taskbar;

public sealed class TaskbarOverlayCoordinator : IDisposable
{
    private readonly ITaskbarAnchorProbe _probe;
    private readonly ITaskbarOverlayWindow _window;
    private readonly ITaskbarPositionLoop _positionLoop;
    private readonly TaskbarClickSequencer _clickSequencer;
    private readonly ITaskbarZOrderWatcher _zOrderWatcher;
    private readonly ITaskbarUiDispatcher _uiDispatcher;
    private readonly ITaskbarRecoveryDelay _recoveryDelay;
    private readonly object _gate = new();
    private bool _started;
    private bool _stopped;
    private bool _disposed;
    private bool _isOverlayAvailable;
    private bool _displaySettingsSubscribed;
    private bool _zOrderSubscribed;
    private bool _recoveryCyclePending;
    private UsageState? _state;

    public TaskbarOverlayCoordinator(
        ITaskbarAnchorProbe probe,
        ITaskbarOverlayWindow window,
        ITaskbarPositionLoop? positionLoop = null,
        ITaskbarClickDelay? clickDelay = null,
        TimeSpan? doubleClickInterval = null,
        ITaskbarZOrderWatcher? zOrderWatcher = null,
        ITaskbarUiDispatcher? uiDispatcher = null,
        ITaskbarRecoveryDelay? recoveryDelay = null)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _positionLoop = positionLoop ?? new DispatcherTimerPositionLoop();
        _clickSequencer = new TaskbarClickSequencer(
            clickDelay ?? new DispatcherTimerClickDelay(),
            doubleClickInterval ?? TimeSpan.FromMilliseconds(System.Windows.Forms.SystemInformation.DoubleClickTime));
        _zOrderWatcher = zOrderWatcher ?? new WindowsTaskbarZOrderWatcher();
        _uiDispatcher = uiDispatcher ?? new DispatcherTaskbarUiDispatcher();
        _recoveryDelay = recoveryDelay ?? new DispatcherTimerRecoveryDelay();

        _window.LeftClick += OnLeftClick;
        _window.DoubleClick += OnDoubleClick;
        _window.RightClickRequested += OnRightClickRequested;
        _clickSequencer.SingleLeftClick += OnSingleLeftClick;
        _clickSequencer.RightClickRequested += OnSequencedRightClick;
    }

    public event Action? SingleLeftClick;

    public event Action? RightClickRequested;

    public event Action<bool>? NotifyIconVisibilityChanged;

    public event Action<bool>? OverlayAvailabilityChanged;

    public bool IsOverlayAvailable
    {
        get
        {
            lock (_gate)
                return _isOverlayAvailable;
        }
    }

    public TaskbarAnchor? CurrentAnchor { get; private set; }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_started)
                return;
            _started = true;
            _stopped = false;
        }

        _positionLoop.Start(PositionOnce);
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            _displaySettingsSubscribed = true;
        }
        catch
        {
            _displaySettingsSubscribed = false;
        }
        PositionOnce();
        try
        {
            _zOrderWatcher.RecoveryRequested += OnZOrderRecoveryRequested;
            _zOrderSubscribed = true;
            _zOrderWatcher.Start();
        }
        catch
        {
            if (_zOrderSubscribed)
            {
                try { _zOrderWatcher.RecoveryRequested -= OnZOrderRecoveryRequested; } catch { }
                _zOrderSubscribed = false;
            }
        }
    }

    public void Update(UsageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        lock (_gate)
        {
            if (_disposed || _stopped)
                return;
            _state = state;
        }

        _window.Update(state);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_stopped)
                return;
            _stopped = true;
            _started = false;
        }

        _positionLoop.Stop();
        _recoveryDelay.Cancel();
        lock (_gate)
            _recoveryCyclePending = false;
        if (_zOrderSubscribed)
        {
            try { _zOrderWatcher.RecoveryRequested -= OnZOrderRecoveryRequested; } catch { }
            _zOrderSubscribed = false;
        }
        try { _zOrderWatcher.Stop(); } catch { }
        if (_displaySettingsSubscribed)
        {
            try { SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged; } catch { }
            _displaySettingsSubscribed = false;
        }
        _clickSequencer.Dispose();
        CurrentAnchor = null;
        _window.Hide();
        SetAvailability(false);
        _window.Close();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        Stop();
        _window.LeftClick -= OnLeftClick;
        _window.DoubleClick -= OnDoubleClick;
        _window.RightClickRequested -= OnRightClickRequested;
        _clickSequencer.SingleLeftClick -= OnSingleLeftClick;
        _clickSequencer.RightClickRequested -= OnSequencedRightClick;
        try { _zOrderWatcher.Dispose(); } catch { }
    }

    private void PositionOnce()
    {
        lock (_gate)
        {
            if (!_started || _stopped || _disposed)
                return;
        }

        try
        {
            if (!_probe.TryGetAnchor(out var anchor))
            {
                CurrentAnchor = null;
                _window.Hide();
                SetAvailability(false);
                return;
            }

            _window.SetAnchor(anchor);
            CurrentAnchor = anchor;
            UsageState? state;
            lock (_gate)
                state = _state;
            if (state is not null)
                _window.Update(state);
            _window.Show();
            SetAvailability(true);
        }
        catch
        {
            CurrentAnchor = null;
            try { _window.Hide(); } catch { }
            SetAvailability(false);
        }
    }

    private void SetAvailability(bool available)
    {
        Action<bool>? handler = null;
        lock (_gate)
        {
            if (_isOverlayAvailable == available)
                return;
            _isOverlayAvailable = available;
            handler = OverlayAvailabilityChanged;
        }

        handler?.Invoke(available);
        NotifyIconVisibilityChanged?.Invoke(!available);
    }

    private void OnLeftClick() => _clickSequencer.OnLeftClick();

    private void OnZOrderRecoveryRequested()
    {
        lock (_gate)
        {
            if (!_started || _stopped || _disposed || !_isOverlayAvailable || _recoveryCyclePending)
                return;
            _recoveryCyclePending = true;
        }

        try
        {
            _uiDispatcher.Post(RunImmediateZOrderRecovery);
        }
        catch
        {
            lock (_gate)
                _recoveryCyclePending = false;
        }
    }

    private void RunImmediateZOrderRecovery()
    {
        lock (_gate)
        {
            if (!_started || _stopped || _disposed || !_isOverlayAvailable)
            {
                _recoveryCyclePending = false;
                return;
            }
        }

        try { _window.ReassertTopmost(); } catch { }
        try
        {
            _recoveryDelay.Schedule(TimeSpan.FromMilliseconds(50), RunTrailingZOrderRecovery);
        }
        catch
        {
            lock (_gate)
                _recoveryCyclePending = false;
        }
    }

    private void RunTrailingZOrderRecovery()
    {
        var shouldRecover = false;
        lock (_gate)
        {
            shouldRecover = _started && !_stopped && !_disposed && _isOverlayAvailable;
            _recoveryCyclePending = false;
        }

        if (shouldRecover)
            try { _window.ReassertTopmost(); } catch { }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => PositionOnce();

    private void OnDoubleClick() => _clickSequencer.OnDoubleClick();

    private void OnRightClickRequested() => _clickSequencer.OnRightClick();

    private void OnSingleLeftClick() => SingleLeftClick?.Invoke();

    private void OnSequencedRightClick() => RightClickRequested?.Invoke();
}

public sealed class TaskbarClickSequencer : IDisposable
{
    private readonly ITaskbarClickDelay _delay;
    private readonly TimeSpan _doubleClickInterval;
    private bool _pending;
    private bool _disposed;

    public TaskbarClickSequencer(ITaskbarClickDelay delay, TimeSpan doubleClickInterval)
    {
        _delay = delay ?? throw new ArgumentNullException(nameof(delay));
        if (doubleClickInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(doubleClickInterval));
        _doubleClickInterval = doubleClickInterval;
    }

    public event Action? SingleLeftClick;

    public event Action? RightClickRequested;

    public void OnLeftClick()
    {
        if (_disposed)
            return;
        if (_pending)
        {
            _delay.Cancel();
            _pending = false;
            return;
        }

        _pending = true;
        _delay.Schedule(_doubleClickInterval, OnSingleClickElapsed);
    }

    public void OnDoubleClick()
    {
        if (_disposed || !_pending)
            return;
        _delay.Cancel();
        _pending = false;
    }

    public void OnRightClick()
    {
        if (_disposed)
            return;
        RightClickRequested?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pending = false;
        _delay.Cancel();
    }

    private void OnSingleClickElapsed()
    {
        if (_disposed || !_pending)
            return;
        _pending = false;
        SingleLeftClick?.Invoke();
    }
}

public sealed class DispatcherTimerPositionLoop : ITaskbarPositionLoop
{
    private readonly TimeSpan _interval;
    private DispatcherTimer? _timer;

    public DispatcherTimerPositionLoop(TimeSpan? interval = null)
    {
        _interval = interval ?? TimeSpan.FromSeconds(2);
    }

    public void Start(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);
        if (_timer is not null)
            return;
        _timer = new DispatcherTimer(_interval, DispatcherPriority.Background, (_, _) => tick(), Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer is null)
            return;
        _timer.Stop();
        _timer = null;
    }
}

public sealed class DispatcherTimerClickDelay : ITaskbarClickDelay
{
    private DispatcherTimer? _timer;

    public void Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Cancel();
        _timer = new DispatcherTimer(delay, DispatcherPriority.Input, (_, _) =>
        {
            var timer = _timer;
            _timer = null;
            timer?.Stop();
            callback();
        }, Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    public void Cancel()
    {
        if (_timer is null)
            return;
        _timer.Stop();
        _timer = null;
    }
}

public sealed class DispatcherTaskbarUiDispatcher : ITaskbarUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public DispatcherTaskbarUiDispatcher(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _dispatcher.BeginInvoke(action, DispatcherPriority.Send);
    }
}

public sealed class DispatcherTimerRecoveryDelay : ITaskbarRecoveryDelay
{
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;

    public DispatcherTimerRecoveryDelay(Dispatcher? dispatcher = null)
    {
        _dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public void Schedule(TimeSpan delay, Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        Cancel();
        _timer = new DispatcherTimer(delay, DispatcherPriority.Send, (_, _) =>
        {
            var timer = _timer;
            _timer = null;
            timer?.Stop();
            callback();
        }, _dispatcher);
        _timer.Start();
    }

    public void Cancel()
    {
        if (_timer is null)
            return;
        _timer.Stop();
        _timer = null;
    }
}
