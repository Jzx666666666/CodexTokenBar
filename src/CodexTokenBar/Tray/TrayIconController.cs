using CodexTokenBar.Domain;
using CodexTokenBar.Monitoring;
using CodexTokenBar.Persistence;
using CodexTokenBar.UI;
using CodexTokenBar.Windows;
using CodexTokenBar.Taskbar;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CodexTokenBar.Tray;

public sealed class TrayIconController : IDisposable
{
    public const string OfficialUsageUrl = "https://chatgpt.com/codex/settings/usage";
    private readonly NotifyIcon _notifyIcon;
    private readonly SummaryWindow _window;
    private readonly UsageMonitor _monitor;
    private readonly IStartupRegistration _startup;
    private readonly IAppStateStore _store;
    private readonly string _executablePath;
    private readonly ToolStripMenuItem _startupItem;
    private readonly SynchronizationContext? _uiContext;
    private readonly System.Windows.Forms.Timer _singleClickTimer;
    private TaskbarOverlayCoordinator? _overlay;
    private Icon? _currentIcon;
    private int _disposed;

    public TrayIconController(
        SummaryWindow window,
        UsageMonitor monitor,
        IStartupRegistration startup,
        IAppStateStore store,
        string executablePath,
        AppSettings settings)
    {
        _window = window ?? throw new ArgumentNullException(nameof(window));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _startup = startup ?? throw new ArgumentNullException(nameof(startup));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _executablePath = executablePath ?? throw new ArgumentNullException(nameof(executablePath));
        _uiContext = SynchronizationContext.Current;

        _startupItem = new ToolStripMenuItem("随 Windows 登录启动")
        {
            Checked = settings.StartWithWindows,
            CheckOnClick = false,
        };
        _startupItem.Click += OnStartupClick;

        var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("立即刷新", null, OnRefreshClick));
        menu.Items.Add(new ToolStripMenuItem("打开官方用量页面", null, OnOpenOfficialUsageClick));
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripMenuItem("清除本地数据", null, OnClearDataClick));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke()));

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Text = "Codex 周额度：正在读取",
            Visible = true,
        };
        _notifyIcon.MouseClick += OnMouseClick;
        _notifyIcon.MouseDoubleClick += OnMouseDoubleClick;
        _singleClickTimer = new System.Windows.Forms.Timer
        {
            Interval = SystemInformation.DoubleClickTime,
        };
        _singleClickTimer.Tick += OnSingleClickElapsed;
        _monitor.StateChanged += OnStateChanged;
        Update(_monitor.State);
    }

    public void AttachOverlay(TaskbarOverlayCoordinator overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (_overlay is not null)
            throw new InvalidOperationException("An overlay is already attached.");
        _overlay = overlay;
        _overlay.SingleLeftClick += OnOverlaySingleClick;
        _overlay.RightClickRequested += OnOverlayRightClick;
        _overlay.NotifyIconVisibilityChanged += OnOverlayNotifyIconVisibilityChanged;
        OnOverlayNotifyIconVisibilityChanged(!overlay.IsOverlayAvailable);
        _overlay.Update(_monitor.State);
    }

    public event Action? ExitRequested;

    private void OnStateChanged(UsageState state)
    {
        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
            _uiContext.Post(_ => Update(state), null);
        else
            Update(state);
    }

    public void Update(UsageState state)
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        var replacement = TrayIconRenderer.RenderIcon(state, GetSystemDpi());
        var previous = _currentIcon;
        _currentIcon = replacement;
        _notifyIcon.Icon = replacement;
        _notifyIcon.Text = TrayIconRenderer.BuildTooltip(state);
        previous?.Dispose();
        _overlay?.Update(state);
    }

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;
        if (_singleClickTimer.Enabled)
        {
            _singleClickTimer.Stop();
            return;
        }
        _singleClickTimer.Start();
    }

    private void OnMouseDoubleClick(object? sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
            _singleClickTimer.Stop();
    }

    private async void OnSingleClickElapsed(object? sender, EventArgs e)
    {
        _singleClickTimer.Stop();
        _window.ToggleNearTray();
        await RefreshSafelyAsync();
    }

    private async void OnOverlaySingleClick()
    {
        if (_overlay?.CurrentAnchor is { } anchor)
            _window.ToggleAboveTaskbar(anchor);
        else
            _window.ToggleNearTray();
        await RefreshSafelyAsync();
    }

    private void OnOverlayRightClick()
    {
        var menu = _notifyIcon.ContextMenuStrip;
        if (menu is null)
            return;
        menu.Show(Cursor.Position);
    }

    private void OnOverlayNotifyIconVisibilityChanged(bool visible) => _notifyIcon.Visible = visible;

    private async void OnRefreshClick(object? sender, EventArgs e)
    {
        await RefreshSafelyAsync();
    }

    private static void OnOpenOfficialUsageClick(object? sender, EventArgs e)
    {
        Process.Start(new ProcessStartInfo(OfficialUsageUrl) { UseShellExecute = true });
    }

    private async void OnStartupClick(object? sender, EventArgs e)
    {
        var enabled = !_startupItem.Checked;
        try
        {
            await _startup.ApplyAsync(enabled, _executablePath, CancellationToken.None);
            await _store.SaveSettingsAsync(new AppSettings(enabled), CancellationToken.None);
            _startupItem.Checked = enabled;
        }
        catch (Exception exception)
        {
            _startupItem.Checked = !enabled;
            BackgroundError?.Invoke(exception);
        }
    }

    private async void OnClearDataClick(object? sender, EventArgs e)
    {
        try
        {
            await _store.ClearAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            BackgroundError?.Invoke(exception);
        }
    }

    public event Action<Exception>? BackgroundError;

    private async Task RefreshSafelyAsync()
    {
        try
        {
            await _monitor.RefreshAsync(CancellationToken.None);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            BackgroundError?.Invoke(exception);
        }
    }

    private static int GetSystemDpi()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return Math.Max(96, (int)Math.Round(graphics.DpiX));
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _monitor.StateChanged -= OnStateChanged;
        if (_overlay is not null)
        {
            _overlay.SingleLeftClick -= OnOverlaySingleClick;
            _overlay.RightClickRequested -= OnOverlayRightClick;
            _overlay.NotifyIconVisibilityChanged -= OnOverlayNotifyIconVisibilityChanged;
            _overlay.Stop();
            _overlay = null;
        }
        _notifyIcon.MouseClick -= OnMouseClick;
        _notifyIcon.MouseDoubleClick -= OnMouseDoubleClick;
        _singleClickTimer.Stop();
        _singleClickTimer.Tick -= OnSingleClickElapsed;
        _singleClickTimer.Dispose();
        _startupItem.Click -= OnStartupClick;
        _notifyIcon.Visible = false;
        _notifyIcon.Icon = null;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _currentIcon?.Dispose();
    }
}
