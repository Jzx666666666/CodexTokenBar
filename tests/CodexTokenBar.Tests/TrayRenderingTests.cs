using CodexTokenBar.Domain;
using CodexTokenBar.Tray;
using System.Drawing;

namespace CodexTokenBar.Tests;

internal static class TrayRenderingTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("Tray.RendersExpectedDpiSizes", RendersExpectedDpiSizes),
        new("Tray.RendersExactToneColors", RendersExactToneColors),
        new("Tray.StaleAlwaysRendersGray", StaleAlwaysRendersGray),
        new("Tray.ThreeDigitValueHasVisibleForeground", ThreeDigitValueHasVisibleForeground),
        new("Tray.TooltipUsesChineseCopyAndFitsNotifyIcon", TooltipUsesChineseCopyAndFitsNotifyIcon),
    ];

    private static Task RendersExpectedDpiSizes()
    {
        foreach (var (dpi, expected) in new[] { (96, 16), (144, 24), (192, 32) })
        {
            using var bitmap = TrayIconRenderer.RenderBitmap(Fresh(47), dpi);
            Assert.Equal(expected, bitmap.Width);
            Assert.Equal(expected, bitmap.Height);
        }
        return Task.CompletedTask;
    }

    private static Task RendersExactToneColors()
    {
        foreach (var (remaining, expected) in new[]
        {
            (47, TrayIconRenderer.Green),
            (30, TrayIconRenderer.Yellow),
            (9, TrayIconRenderer.Red),
        })
        {
            using var bitmap = TrayIconRenderer.RenderBitmap(Fresh(remaining), 192);
            Assert.Equal(expected.ToArgb(), bitmap.GetPixel(2, 16).ToArgb());
        }
        return Task.CompletedTask;
    }

    private static Task StaleAlwaysRendersGray()
    {
        using var bitmap = TrayIconRenderer.RenderBitmap(Fresh(80) with
        {
            Freshness = UsageFreshness.Stale,
            ErrorMessage = "broken",
        }, 192);
        Assert.Equal(TrayIconRenderer.Gray.ToArgb(), bitmap.GetPixel(2, 16).ToArgb());
        return Task.CompletedTask;
    }

    private static Task ThreeDigitValueHasVisibleForeground()
    {
        using var bitmap = TrayIconRenderer.RenderBitmap(Fresh(100), 192);
        var background = TrayIconRenderer.Green.ToArgb();
        var foregroundCount = 0;
        for (var y = 2; y < bitmap.Height - 2; y++)
        for (var x = 1; x < bitmap.Width - 1; x++)
        {
            var pixel = bitmap.GetPixel(x, y).ToArgb();
            if (pixel != background && Color.FromArgb(pixel).A > 0)
                foregroundCount++;
        }
        Assert.Equal(true, foregroundCount > 20);
        return Task.CompletedTask;
    }

    private static Task TooltipUsesChineseCopyAndFitsNotifyIcon()
    {
        var tooltip = TrayIconRenderer.BuildTooltip(Fresh(47), TimeZoneInfo.CreateCustomTimeZone(
            "China Test Time", TimeSpan.FromHours(8), "China Test Time", "China Test Time"));
        Assert.Equal(true, tooltip.Contains("Codex 周额度：剩余 47%", StringComparison.Ordinal));
        Assert.Equal(true, tooltip.Contains("重置：8月19日 16:30", StringComparison.Ordinal));
        Assert.Equal(true, tooltip.Length <= 63);
        return Task.CompletedTask;
    }

    private static UsageState Fresh(int remaining) => new(
        UsageFreshness.Fresh,
        new QuotaView(
            "codex",
            "codex",
            remaining,
            new DateTimeOffset(2026, 8, 19, 8, 30, 0, TimeSpan.Zero)),
        Array.Empty<QuotaView>(),
        new DateTimeOffset(2026, 8, 12, 8, 42, 0, TimeSpan.Zero),
        null,
        null);
}
