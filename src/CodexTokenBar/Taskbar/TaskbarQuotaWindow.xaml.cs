using CodexTokenBar.Domain;
using CodexTokenBar.Tray;
using Microsoft.Win32;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace CodexTokenBar.Taskbar;

public partial class TaskbarQuotaWindow : Window, ITaskbarOverlayWindow
{
    private const int GwlExStyle = -20;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int WmMouseActivate = 0x0021;
    private const int MaNoActivate = 3;
    private const string PersonalizeRegistryPath =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string SystemUsesLightThemeValue = "SystemUsesLightTheme";
    private int _dpi = 96;
    private bool _closed;
    private TaskbarWindowPlacement? _placement;
    private TaskbarVisualMetrics? _metrics;

    public TaskbarQuotaWindow()
    {
        InitializeComponent();
        ToolTipService.SetShowDuration(this, int.MaxValue);
    }

    public event Action? LeftClick;

    public event Action? DoubleClick;

    public event Action? RightClickRequested;

    public void SetAnchor(TaskbarAnchor anchor)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!TaskbarWindowPlacementCalculator.TryFromAnchor(anchor, out var placement))
            throw new ArgumentException("The taskbar anchor is not a valid physical rectangle.", nameof(anchor));
        if (!TaskbarVisualMetricsCalculator.TryCalculate(placement.Height, placement.Dpi, out var metrics))
            throw new InvalidOperationException("The taskbar is too short for a 24 DIP quota ring.");

        _placement = placement;
        _dpi = placement.Dpi;
        _metrics = metrics;
        Width = placement.WidthDip;
        Height = placement.HeightDip;
        RingImage.Width = metrics.RingDiameterDip;
        RingImage.Height = metrics.RingDiameterDip;
        Surface.CornerRadius = new CornerRadius(metrics.CornerRadiusDip);
        _ = new WindowInteropHelper(this).EnsureHandle();
        UpdateLayout();
        ApplyPhysicalPlacement(showWindow: false);
    }

    public new void Show()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        if (!IsVisible)
            base.Show();
        Topmost = true;
        ApplyPhysicalPlacement(showWindow: true);
    }

    public new void Hide()
    {
        if (IsVisible)
            base.Hide();
    }

    public void ReassertTopmost()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        ApplyPhysicalPlacement(showWindow: false);
    }

    public void Update(UsageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_metrics is not { } metrics)
            return;

        var presentation = TaskbarQuotaPresentation.Create(
            state,
            metrics,
            useLightText: ShouldUseLightText());
        RingImage.Source = TaskbarRingRenderer.ToBitmapSource(state, _dpi, metrics.RingDiameterDip);
        RingText.Text = presentation.Text;
        RingText.FontSize = presentation.FontSizeDip;
        RingText.SetResourceReference(
            TextBlock.ForegroundProperty,
            presentation.Tone == TaskbarTextTone.Light
                ? System.Windows.SystemColors.HighlightTextBrushKey
                : System.Windows.SystemColors.WindowTextBrushKey);
        ToolTip = TrayIconRenderer.BuildTooltip(state);
    }

    public new void Close()
    {
        if (_closed)
            return;
        _closed = true;
        if (IsVisible)
            base.Hide();
        base.Close();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(hwnd, GwlExStyle).ToInt64();
        style |= WsExNoActivate | WsExToolWindow;
        SetWindowLongPtr(hwnd, GwlExStyle, new IntPtr(style));
        if (HwndSource.FromHwnd(hwnd) is { } source)
            source.AddHook(WindowMessageHook);
    }

    private void ApplyPhysicalPlacement(bool showWindow)
    {
        if (_placement is not { } placement)
            return;

        var command = TaskbarWindowPlacementCalculator.CreateSetWindowPosCommand(
            placement,
            showWindow);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero ||
            !SetWindowPos(
                hwnd,
                command.HwndInsertAfter,
                command.Placement.Left,
                command.Placement.Top,
                command.Placement.Width,
                command.Placement.Height,
                command.Flags))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowPos failed for taskbar overlay.");
        }
    }

    private IntPtr WindowMessageHook(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmMouseActivate)
        {
            handled = true;
            return new IntPtr(MaNoActivate);
        }

        return IntPtr.Zero;
    }

    private void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
            DoubleClick?.Invoke();
        else
            LeftClick?.Invoke();
        e.Handled = true;
    }

    private void OnMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        RightClickRequested?.Invoke();
        e.Handled = true;
    }

    private static bool ShouldUseLightText()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeRegistryPath, writable: false);
            if (key?.GetValue(SystemUsesLightThemeValue) is int value && value is 0 or 1)
                return value == 0;
        }
        catch
        {
        }

        // Unknown taskbar theme: keep the conservative dark/system text role.
        return false;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
