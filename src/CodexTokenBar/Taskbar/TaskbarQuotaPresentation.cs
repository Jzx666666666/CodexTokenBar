using CodexTokenBar.Domain;

namespace CodexTokenBar.Taskbar;

public enum TaskbarTextTone
{
    Light,
    Dark,
}

public sealed record TaskbarQuotaPresentation(
    string Text,
    double FontSizeDip,
    TaskbarTextTone Tone)
{
    public const string FontFamilyName = "Segoe UI Variable Text, Segoe UI";
    public const string FontWeightName = "SemiBold";

    public static TaskbarQuotaPresentation Create(
        UsageState state,
        TaskbarVisualMetrics metrics,
        bool useLightText)
    {
        ArgumentNullException.ThrowIfNull(state);

        var text = TaskbarRingRenderer.GetCenterText(state);
        var fontSizeDip = text == "100%"
            ? metrics.CompactFontSizeDip
            : metrics.FontSizeDip;
        var tone = useLightText ? TaskbarTextTone.Light : TaskbarTextTone.Dark;
        return new TaskbarQuotaPresentation(text, fontSizeDip, tone);
    }
}
