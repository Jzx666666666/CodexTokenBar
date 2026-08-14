using System.Drawing;
using CodexTokenBar.Domain;
using CodexTokenBar.Taskbar;

namespace CodexTokenBar.Tests;

internal static class TaskbarRingRenderingTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("TaskbarVisualMetrics_UsesLiteralAdaptiveDiametersAndClamps", async () =>
        {
            foreach (var (heightPixels, dpi, expectedDiameter) in new[]
            {
                (32, 96, 24d),
                (48, 96, 40d),
                (50, 120, 32d),
                (72, 144, 40d),
                (120, 192, 40d),
            })
            {
                Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(
                    heightPixels,
                    dpi,
                    out var metrics));
                Assert.Equal(expectedDiameter, metrics.RingDiameterDip);

                var availableDiameter = heightPixels * 96d / dpi - 8d;
                Require(
                    metrics.RingDiameterDip <= availableDiameter,
                    $"{heightPixels}px at {dpi} DPI exceeds the available diameter.");
                Require(metrics.RingDiameterDip <= 40d, "The adaptive diameter must cap at 40 DIP.");
            }

            Assert.Equal(false, TaskbarVisualMetricsCalculator.TryCalculate(30, 144, out _));
            await Task.CompletedTask;
        }),
        new("TaskbarRingRenderer_RenderBitmap_UsesRequestedPhysicalSize", async () =>
        {
            foreach (var (dpi, expectedSize) in new[]
            {
                (96, 40),
                (144, 60),
                (192, 80),
            })
            {
                using var bitmap = TaskbarRingRenderer.RenderBitmap(FreshState(37), dpi, 40d);

                Assert.Equal(expectedSize, bitmap.Width);
                Assert.Equal(expectedSize, bitmap.Height);
                Assert.Equal(0, bitmap.GetPixel(0, 0).A);
                Assert.Equal(0, bitmap.GetPixel(expectedSize - 1, expectedSize - 1).A);
            }

            await Task.CompletedTask;
        }),
        new("TaskbarRingRenderer_RenderBitmap_ProbesConsumedArcEndpointAt61_2DegreesFor83Percent", async () =>
        {
            using var bitmap = TaskbarRingRenderer.RenderBitmap(FreshState(83), 96, 40d);

            Require(HasGray(bitmap, 0), "83% remaining should start with gray at 12 o'clock.");
            AssertConsumedArcEndpoint(bitmap, 61.2d, IsGreen, "83% remaining");
            await Task.CompletedTask;
        }),
        new("TaskbarRingRenderer_RenderBitmap_ProbesConsumedArcEndpointAt226_8DegreesFor37Percent", async () =>
        {
            using var bitmap = TaskbarRingRenderer.RenderBitmap(FreshState(37), 96, 40d);

            Require(HasGray(bitmap, 0), "37% remaining should start with gray at 12 o'clock.");
            AssertConsumedArcEndpoint(bitmap, 226.8d, IsGreen, "37% remaining");
            await Task.CompletedTask;
        }),
        new("TaskbarRingRenderer_RenderBitmap_ProbesConsumedArcEndpointAt327_6DegreesFor9Percent", async () =>
        {
            using var bitmap = TaskbarRingRenderer.RenderBitmap(FreshState(9), 96, 40d);

            Require(HasGray(bitmap, 0), "9% remaining should start with gray at 12 o'clock.");
            AssertConsumedArcEndpoint(bitmap, 327.6d, IsRed, "9% remaining");
            await Task.CompletedTask;
        }),
        new("TaskbarRingRenderer_StaleState_UsesOnlyGrayRingAndKeepsCenterTextContract", async () =>
        {
            using var bitmap = TaskbarRingRenderer.RenderBitmap(StaleState(75), 96, 40d);

            Require(HasGray(bitmap, 0), "stale state should keep a gray ring.");
            Require(!HasTrustedColor(bitmap), "stale state should not expose a trustworthy color arc.");
            Assert.Equal("\u2014", TaskbarRingRenderer.GetCenterText(StaleState(75)));
            Assert.Equal("75%", TaskbarRingRenderer.GetCenterText(FreshState(75)));
            await Task.CompletedTask;
        }),
        new("TaskbarRingRenderer_TrustedBitmap_HasFullyTransparentCenterInterior", async () =>
        {
            using var bitmap = TaskbarRingRenderer.RenderBitmap(FreshState(83), 96, 40d);

            AssertCenterInteriorIsTransparent(bitmap);
            await Task.CompletedTask;
        }),
        new("TaskbarRingRenderer_ToBitmapSource_PreservesDpiScaledSize", async () =>
        {
            foreach (var (dpi, expectedSize) in new[]
            {
                (96, 40),
                (144, 60),
                (192, 80),
            })
            {
                var bitmapSource = TaskbarRingRenderer.ToBitmapSource(FreshState(37), dpi, 40d);

                Require(bitmapSource is not null, "ToBitmapSource should return a bitmap source.");
                Assert.Equal(expectedSize, bitmapSource!.PixelWidth);
                Assert.Equal(expectedSize, bitmapSource.PixelHeight);
            }

            await Task.CompletedTask;
        }),
    ];

    private static UsageState FreshState(int remainingPercent) => new(
        UsageFreshness.Fresh,
        new QuotaView("primary", "Primary", remainingPercent, null),
        Array.Empty<QuotaView>(),
        DateTimeOffset.UtcNow,
        null,
        null);

    private static UsageState StaleState(int remainingPercent) => new(
        UsageFreshness.Stale,
        new QuotaView("primary", "Primary", remainingPercent, null),
        Array.Empty<QuotaView>(),
        DateTimeOffset.UtcNow.AddMinutes(-5),
        "stale",
        DateTimeOffset.UtcNow);

    private static bool HasGreen(Bitmap bitmap, double angle) => HasColor(bitmap, angle, IsGreen);

    private static bool HasRed(Bitmap bitmap, double angle) => HasColor(bitmap, angle, IsRed);

    private static bool HasYellow(Bitmap bitmap, double angle) => HasColor(bitmap, angle, IsYellow);

    private static bool HasGray(Bitmap bitmap, double angle) => HasColor(bitmap, angle, IsGray);

    private static void AssertConsumedArcEndpoint(
        Bitmap bitmap,
        double expectedEndpoint,
        Func<Color, bool> remainderPredicate,
        string description)
    {
        const double beforeEndpointOffset = 2d;
        const double afterEndpointOffset = 10d;
        const double angularTolerance = 1.5d;

        Require(
            HasColor(bitmap, expectedEndpoint - beforeEndpointOffset, IsGray, angularTolerance),
            $"{description} should remain gray just before {expectedEndpoint:0.0} degrees.");
        Require(
            HasColor(bitmap, expectedEndpoint + afterEndpointOffset, remainderPredicate, angularTolerance),
            $"{description} should show its threshold color just after {expectedEndpoint:0.0} degrees.");
    }

    private static bool HasColor(Bitmap bitmap, double angle, Func<Color, bool> predicate)
        => HasColor(bitmap, angle, predicate, 8d);

    private static bool HasColor(
        Bitmap bitmap,
        double angle,
        Func<Color, bool> predicate,
        double angularTolerance)
    {
        var center = (bitmap.Width - 1) / 2d;
        var targetRadius = bitmap.Width / 2d - 3d;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var dx = x - center;
                var dy = y - center;
                var radius = Math.Sqrt(dx * dx + dy * dy);
                if (Math.Abs(radius - targetRadius) > 1.5d)
                    continue;

                var pixelAngle = Math.Atan2(dx, -dy) * 180d / Math.PI;
                if (pixelAngle < 0)
                    pixelAngle += 360d;

                var angularDistance = Math.Abs(pixelAngle - angle);
                angularDistance = Math.Min(angularDistance, 360d - angularDistance);
                if (angularDistance <= angularTolerance && predicate(bitmap.GetPixel(x, y)))
                    return true;
            }
        }

        return false;
    }

    private static bool HasTrustedColor(Bitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var color = bitmap.GetPixel(x, y);
                if (IsGreen(color) || IsYellow(color) || IsRed(color))
                    return true;
            }
        }

        return false;
    }

    private static void AssertCenterInteriorIsTransparent(Bitmap bitmap)
    {
        var center = (bitmap.Width - 1) / 2d;
        var safeCenterRadius = bitmap.Width / 2d - 8d;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var dx = x - center;
                var dy = y - center;
                if (Math.Sqrt(dx * dx + dy * dy) > safeCenterRadius)
                    continue;

                var color = bitmap.GetPixel(x, y);
                Assert.Equal(0, color.A);
            }
        }
    }

    private static bool IsGreen(Color color) =>
        color.A > 0 && color.G > color.R + 35 && color.G > color.B + 20;

    private static bool IsRed(Color color) =>
        color.A > 0 && color.R > color.G + 35 && color.R > color.B + 35;

    private static bool IsYellow(Color color) =>
        color.A > 0 && color.R > 150 && color.G > 120 && color.B < 120 && Math.Abs(color.R - color.G) < 100;

    private static bool IsGray(Color color) =>
        color.A > 0 && Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B)) <= 12;

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
