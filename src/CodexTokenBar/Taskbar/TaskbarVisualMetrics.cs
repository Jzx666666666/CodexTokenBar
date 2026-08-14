namespace CodexTokenBar.Taskbar;

public readonly record struct TaskbarVisualMetrics(
    double RingDiameterDip,
    double StrokeWidthDip,
    double FontSizeDip,
    double CompactFontSizeDip,
    double CornerRadiusDip);

public static class TaskbarVisualMetricsCalculator
{
    public static bool TryCalculate(
        int taskbarHeightPixels,
        int dpi,
        out TaskbarVisualMetrics metrics)
    {
        metrics = default;
        if (dpi <= 0 || taskbarHeightPixels <= 0)
            return false;

        var heightDip = taskbarHeightPixels * 96d / dpi;
        var diameter = Math.Min(40d, heightDip - 8d);
        if (diameter < 24d)
            return false;

        var strokeWidth = Math.Clamp(diameter * 0.1d, 2.5d, 4d);
        var fontSize = Math.Clamp(diameter * 0.3d, 8d, 12d);
        var compactFontSize = Math.Clamp(diameter * 0.24d, 7d, 10d);
        var cornerRadius = Math.Clamp(diameter * 0.1d, 3d, 4d);
        metrics = new TaskbarVisualMetrics(
            diameter,
            strokeWidth,
            fontSize,
            compactFontSize,
            cornerRadius);
        return true;
    }
}
