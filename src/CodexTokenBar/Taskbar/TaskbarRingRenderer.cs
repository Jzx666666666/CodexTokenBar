using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using CodexTokenBar.Domain;

namespace CodexTokenBar.Taskbar;

public static class TaskbarRingRenderer
{
    private const int ReferenceDpi = 96;
    private static readonly Color TrackColor = Color.FromArgb(255, 96, 96, 104);
    private static readonly Color GreenColor = Color.FromArgb(255, 49, 198, 91);
    private static readonly Color YellowColor = Color.FromArgb(255, 239, 186, 57);
    private static readonly Color RedColor = Color.FromArgb(255, 218, 70, 70);

    public static Bitmap RenderBitmap(UsageState state, int dpi, double diameterDip)
    {
        ArgumentNullException.ThrowIfNull(state);
        var size = ScaleDipToPixels(diameterDip, dpi);
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppPArgb);

        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.Clear(Color.Transparent);

        var strokeWidthDip = Math.Clamp(diameterDip * 0.1d, 2.5d, 4d);
        var strokeWidth = (float)(strokeWidthDip * dpi / (double)ReferenceDpi);
        var inset = strokeWidth / 2f + 1f;
        var bounds = new RectangleF(
            inset,
            inset,
            size - inset * 2f,
            size - inset * 2f);

        if (TryGetTrustedRemainingPercent(state, out var remainingPercent))
        {
            using var thresholdPen = CreatePen(GetForegroundColor(remainingPercent), strokeWidth);
            graphics.DrawEllipse(thresholdPen, bounds);

            using var consumedPen = CreatePen(TrackColor, strokeWidth);
            var consumedSweep = (100 - remainingPercent) * 3.6f;
            if (consumedSweep >= 360f)
            {
                graphics.DrawEllipse(consumedPen, bounds);
            }
            else if (consumedSweep > 0f)
            {
                graphics.DrawArc(consumedPen, bounds, -90f, consumedSweep);
            }
        }

        else
        {
            using var trackPen = CreatePen(TrackColor, strokeWidth);
            graphics.DrawEllipse(trackPen, bounds);
        }

        return bitmap;
    }

    public static Bitmap RenderBitmap(UsageState state, int dpi) => RenderBitmap(state, dpi, 40d);

    public static string GetCenterText(UsageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return TryGetTrustedRemainingPercent(state, out var remainingPercent)
            ? $"{remainingPercent}%"
            : "\u2014";
    }

    public static BitmapSource ToBitmapSource(UsageState state, int dpi, double diameterDip)
    {
        byte[] pngBytes;
        using (var bitmap = RenderBitmap(state, dpi, diameterDip))
        using (var stream = new MemoryStream())
        {
            bitmap.Save(stream, ImageFormat.Png);
            pngBytes = stream.ToArray();
        }

        using var sourceStream = new MemoryStream(pngBytes, writable: false);
        var source = BitmapFrame.Create(
            sourceStream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        source.Freeze();
        return source;
    }

    public static BitmapSource ToBitmapSource(UsageState state, int dpi) => ToBitmapSource(state, dpi, 40d);

    private static int ScaleDipToPixels(double dip, int dpi)
    {
        if (dpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "DPI must be positive.");
        if (!double.IsFinite(dip) || dip <= 0d)
            throw new ArgumentOutOfRangeException(nameof(dip), dip, "Diameter must be finite and positive.");

        var pixels = Math.Round(dip * (double)dpi / ReferenceDpi, MidpointRounding.AwayFromZero);
        if (pixels <= 0 || pixels > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(dpi), dpi, "The scaled bitmap is not representable.");

        return (int)pixels;
    }

    private static bool TryGetTrustedRemainingPercent(UsageState state, out int remainingPercent)
    {
        remainingPercent = 0;
        if (state.Freshness is not (UsageFreshness.Fresh or UsageFreshness.Refreshing) ||
            state.PrimaryQuota is null)
        {
            return false;
        }

        remainingPercent = state.PrimaryQuota.RemainingPercent;
        return remainingPercent is >= 0 and <= 100;
    }

    private static Color GetForegroundColor(int remainingPercent) =>
        remainingPercent > 30
            ? GreenColor
            : remainingPercent >= 10
                ? YellowColor
                : RedColor;

    private static Pen CreatePen(Color color, float width) => new(color, width)
    {
        StartCap = LineCap.Round,
        EndCap = LineCap.Round,
        LineJoin = LineJoin.Round,
    };
}
