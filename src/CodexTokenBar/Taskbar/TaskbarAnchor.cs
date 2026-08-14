namespace CodexTokenBar.Taskbar;

public readonly record struct PixelRect(int Left, int Top, int Width, int Height)
{
    public int Right => checked(Left + Width);

    public int Bottom => checked(Top + Height);

    public long RightExclusive => (long)Left + Width;

    public long BottomExclusive => (long)Top + Height;

    public bool IsValid =>
        Width > 0 &&
        Height > 0 &&
        RightExclusive <= int.MaxValue &&
        BottomExclusive <= int.MaxValue;

    public bool Contains(PixelRect other) =>
        IsValid &&
        other.IsValid &&
        other.Left >= Left &&
        other.Top >= Top &&
        other.RightExclusive <= RightExclusive &&
        other.BottomExclusive <= BottomExclusive;
}

public sealed record TaskbarAnchor(int Left, int Top, int Width, int Height)
{
    public PixelRect PixelSourceRect => new(Left, Top, Width, Height);

    public PixelRect SourcePixelRect => PixelSourceRect;

    public PixelRect PixelRect => PixelSourceRect;

    public int Dpi => Width > 0
        ? checked((int)Math.Round(Width * 96d / 64d, MidpointRounding.AwayFromZero))
        : 96;

    public double DipLeft => Width > 0 ? Left * 64d / Width : 0d;

    public double DipTop => Width > 0 ? Top * 64d / Width : 0d;

    public double DipWidth => Width > 0 ? Width * 64d / Width : 0d;

    public double DipHeight => Width > 0 ? Height * 64d / Width : 0d;

    public double LeftDip => DipLeft;

    public double TopDip => DipTop;

    public double WidthDip => DipWidth;

    public double HeightDip => DipHeight;
}
