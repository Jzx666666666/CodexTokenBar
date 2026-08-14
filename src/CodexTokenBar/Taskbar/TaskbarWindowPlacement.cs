namespace CodexTokenBar.Taskbar;

public static class TaskbarWindowPlacementFlags
{
    public const uint NoActivate = 0x0010;
    public const uint NoZOrder = 0x0004;
    public const uint NoOwnerZOrder = 0x0200;
    public const uint ShowWindow = 0x0040;
}

public readonly record struct TaskbarWindowPlacement(
    int Left,
    int Top,
    int Width,
    int Height,
    int Dpi)
{
    public PixelRect PixelRect => new(Left, Top, Width, Height);

    public double WidthDip => Width * 96d / Dpi;

    public double HeightDip => Height * 96d / Dpi;
}

public readonly record struct TaskbarWindowPlacementCommand(
    TaskbarWindowPlacement Placement,
    IntPtr HwndInsertAfter,
    uint Flags)
{
    public bool RequestsTopmost => HwndInsertAfter == new IntPtr(-1);

    public bool DoesNotActivate => (Flags & TaskbarWindowPlacementFlags.NoActivate) != 0;

    public bool PreservesOwnerZOrder => (Flags & TaskbarWindowPlacementFlags.NoOwnerZOrder) != 0;

    public bool RequestsShow => (Flags & TaskbarWindowPlacementFlags.ShowWindow) != 0;

    public bool DoesNotSuppressZOrder => (Flags & TaskbarWindowPlacementFlags.NoZOrder) == 0;
}

public static class TaskbarWindowPlacementCalculator
{
    public static readonly IntPtr HwndTopmost = new(-1);

    public static bool TryFromAnchor(
        TaskbarAnchor? anchor,
        out TaskbarWindowPlacement placement)
    {
        placement = default;
        if (anchor is null || !anchor.PixelSourceRect.IsValid || anchor.Dpi <= 0)
            return false;

        placement = new TaskbarWindowPlacement(
            anchor.Left,
            anchor.Top,
            anchor.Width,
            anchor.Height,
            anchor.Dpi);
        return true;
    }

    public static TaskbarWindowPlacementCommand CreateSetWindowPosCommand(
        TaskbarWindowPlacement placement,
        bool showWindow)
    {
        var flags = TaskbarWindowPlacementFlags.NoActivate |
            TaskbarWindowPlacementFlags.NoOwnerZOrder;
        if (showWindow)
            flags |= TaskbarWindowPlacementFlags.ShowWindow;

        return new TaskbarWindowPlacementCommand(placement, HwndTopmost, flags);
    }
}
