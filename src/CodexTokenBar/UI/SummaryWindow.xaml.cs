using Microsoft.Win32;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CodexTokenBar.Taskbar;

namespace CodexTokenBar.UI;

public readonly record struct SummaryWindowPlacement(double Left, double Top);

public static class SummaryWindowPlacementCalculator
{
    public static bool TryCalculate(
        TaskbarAnchor anchor,
        PixelRect primaryWorkingArea,
        double summaryWidthDip,
        double summaryHeightDip,
        out SummaryWindowPlacement placement)
    {
        placement = default;
        if (!anchor.PixelSourceRect.IsValid ||
            anchor.Dpi <= 0 ||
            !primaryWorkingArea.IsValid ||
            summaryWidthDip <= 0 ||
            summaryHeightDip <= 0)
        {
            return false;
        }

        var pixelsToDip = 96d / anchor.Dpi;
        var anchorLeftDip = anchor.Left * pixelsToDip;
        var anchorTopDip = anchor.Top * pixelsToDip;
        var workLeftDip = primaryWorkingArea.Left * pixelsToDip;
        var workTopDip = primaryWorkingArea.Top * pixelsToDip;
        var workRightDip = primaryWorkingArea.Right * pixelsToDip;
        var workBottomDip = primaryWorkingArea.Bottom * pixelsToDip;
        var left = Clamp(
            anchorLeftDip + (anchor.DipWidth - summaryWidthDip) / 2d,
            workLeftDip,
            workRightDip - summaryWidthDip);
        var top = Clamp(
            anchorTopDip - summaryHeightDip - 8d,
            workTopDip,
            workBottomDip - summaryHeightDip);

        placement = new SummaryWindowPlacement(left, top);
        return true;
    }

    private static double Clamp(double value, double minimum, double maximum) =>
        maximum < minimum ? minimum : Math.Clamp(value, minimum, maximum);
}

public partial class SummaryWindow : Window
{
    private bool _allowClose;
    private readonly bool? _darkModeOverride;

    public SummaryWindow(SummaryViewModel viewModel, bool? darkModeOverride = null)
    {
        InitializeComponent();
        _darkModeOverride = darkModeOverride;
        DataContext = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        ApplySystemTheme();
    }

    public void ShowNearTray()
    {
        ApplySystemTheme();
        Topmost = true;
        if (!IsVisible)
            Show();
        WindowState = WindowState.Normal;
        PositionNearPointer();
        Activate();
        HideButton.Focus();
    }

    public void ToggleNearTray()
    {
        if (IsVisible)
            Hide();
        else
            ShowNearTray();
    }

    public void ToggleAboveTaskbar(TaskbarAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        ApplySystemTheme();
        if (IsVisible)
        {
            Hide();
            return;
        }

        Topmost = true;
        if (!IsVisible)
            Show();
        WindowState = WindowState.Normal;
        UpdateLayout();
        var screen = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
        if (screen is null)
            return;
        if (!SummaryWindowPlacementCalculator.TryCalculate(
                anchor,
                new PixelRect(screen.Value.Left, screen.Value.Top, screen.Value.Width, screen.Value.Height),
                ActualWidth,
                ActualHeight,
                out var placement))
        {
            return;
        }

        Left = placement.Left;
        Top = placement.Top;
        Activate();
        HideButton.Focus();
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void PositionNearPointer()
    {
        var pointer = System.Windows.Forms.Cursor.Position;
        var screen = System.Windows.Forms.Screen.FromPoint(pointer);
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var bottomRight = transform.Transform(new System.Windows.Point(
            screen.WorkingArea.Right,
            screen.WorkingArea.Bottom));
        var topLeft = transform.Transform(new System.Windows.Point(
            screen.WorkingArea.Left,
            screen.WorkingArea.Top));
        Left = Math.Max(topLeft.X + 12, bottomRight.X - ActualWidth - 12);
        Top = Math.Max(topLeft.Y + 12, bottomRight.Y - ActualHeight - 12);
    }

    private void ApplySystemTheme()
    {
        var dark = _darkModeOverride ?? IsDarkMode();
        Resources["PanelBrush"] = Brush(dark ? "#FF201F1E" : "#FFFDFDFD");
        Resources["TextPrimaryBrush"] = Brush(dark ? "#FFF5F5F5" : "#FF1B1A19");
        Resources["TextSecondaryBrush"] = Brush(dark ? "#FFC8C6C4" : "#FF5B5A58");
        Resources["DividerBrush"] = Brush(dark ? "#FF3D3B39" : "#FFE5E3E1");
        Resources["ErrorSurfaceBrush"] = Brush(dark ? "#FF4A211D" : "#FFFDE7E5");
        Resources["ErrorTextBrush"] = Brush(dark ? "#FFFFB4AB" : "#FF8A1C13");
    }

    private static SolidColorBrush Brush(string value) =>
        new((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(value));

    private static bool IsDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return false;
        }
    }

    private void OnHideClick(object sender, RoutedEventArgs e) => Hide();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
            return;
        e.Cancel = true;
        Hide();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible)
            Topmost = false;
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Hide();
    }
}
