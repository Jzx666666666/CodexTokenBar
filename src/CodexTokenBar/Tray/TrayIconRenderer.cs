using CodexTokenBar.Domain;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace CodexTokenBar.Tray;

public static class TrayIconRenderer
{
    public static readonly Color Green = Color.FromArgb(16, 124, 16);
    public static readonly Color Yellow = Color.FromArgb(249, 168, 37);
    public static readonly Color Red = Color.FromArgb(196, 43, 28);
    public static readonly Color Gray = Color.FromArgb(96, 94, 92);

    public static Bitmap RenderBitmap(UsageState state, int dpi)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (dpi <= 0)
            throw new ArgumentOutOfRangeException(nameof(dpi));

        var size = Math.Max(16, (int)Math.Round(16d * dpi / 96d));
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        bitmap.SetResolution(dpi, dpi);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Color.Transparent);

        var trusted = state.Freshness is UsageFreshness.Fresh or UsageFreshness.Refreshing;
        var remaining = trusted ? state.PrimaryQuota?.RemainingPercent : null;
        var tone = QuotaPresentation.GetTone(remaining);
        var background = GetColor(tone);
        var radius = Math.Max(3f, size * .22f);
        using var path = RoundedRectangle(new RectangleF(0, 0, size, size), radius);
        using var brush = new SolidBrush(background);
        graphics.FillPath(brush, path);

        var text = remaining?.ToString() ?? "—";
        var foreground = tone == QuotaTone.Yellow ? Color.FromArgb(31, 31, 31) : Color.White;
        var fontSize = text.Length >= 3 ? size * .38f : size * .48f;
        using var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(foreground);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap,
        };
        var bounds = new RectangleF(0, text == "—" ? -size * .06f : 0, size, size);
        graphics.DrawString(text, font, textBrush, bounds, format);
        return bitmap;
    }

    public static Icon RenderIcon(UsageState state, int dpi)
    {
        using var bitmap = RenderBitmap(state, dpi);
        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    public static string BuildTooltip(UsageState state, TimeZoneInfo? localTimeZone = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        var trusted = state.Freshness is UsageFreshness.Fresh or UsageFreshness.Refreshing;
        if (!trusted || state.PrimaryQuota is null)
            return "Codex 周额度：数据不可用";

        var firstLine = $"Codex 周额度：剩余 {state.PrimaryQuota.RemainingPercent}%";
        if (state.PrimaryQuota.ResetsAt is not { } reset)
            return firstLine;
        var local = TimeZoneInfo.ConvertTime(reset, localTimeZone ?? TimeZoneInfo.Local);
        var value = $"{firstLine}\n重置：{local:M月d日 HH:mm}";
        return value.Length <= 63 ? value : value[..63];
    }

    private static Color GetColor(QuotaTone tone) => tone switch
    {
        QuotaTone.Green => Green,
        QuotaTone.Yellow => Yellow,
        QuotaTone.Red => Red,
        _ => Gray,
    };

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
