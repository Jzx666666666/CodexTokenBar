namespace CodexTokenBar.Taskbar;

public static class TaskbarAnchorCalculator
{
    private const int SlotWidthDip = 64;
    private const int ReferenceDpi = 96;

    public static bool TryCalculateFromNotificationBoundary(
        PixelRect primaryScreen,
        PixelRect taskbar,
        PixelRect notificationArea,
        int dpi,
        out TaskbarAnchor anchor)
    {
        anchor = null!;

        if (!notificationArea.IsValid ||
            !taskbar.IsValid ||
            !taskbar.Contains(notificationArea))
        {
            return false;
        }

        var syntheticWidth = Math.Min(notificationArea.Width, taskbar.Width);
        if (syntheticWidth <= 0)
            return false;

        var syntheticChevron = new PixelRect(
            notificationArea.Left,
            taskbar.Top,
            syntheticWidth,
            taskbar.Height);

        return TryCalculate(
            primaryScreen,
            taskbar,
            syntheticChevron,
            dpi,
            out anchor);
    }

    public static bool TryCalculate(
        PixelRect primaryScreen,
        PixelRect taskbar,
        PixelRect chevron,
        int dpi,
        out TaskbarAnchor anchor)
    {
        anchor = null!;

        if (dpi <= 0 ||
            !primaryScreen.IsValid ||
            !taskbar.IsValid ||
            !chevron.IsValid)
        {
            return false;
        }

        if (taskbar.Left < primaryScreen.Left ||
            taskbar.Top < primaryScreen.Top ||
            taskbar.RightExclusive > primaryScreen.RightExclusive ||
            taskbar.BottomExclusive != primaryScreen.BottomExclusive ||
            taskbar.Width <= taskbar.Height)
        {
            return false;
        }

        if (!taskbar.Contains(chevron))
        {
            return false;
        }

        var slotWidth = (long)Math.Round(
            SlotWidthDip * (double)dpi / ReferenceDpi,
            MidpointRounding.AwayFromZero);

        if (slotWidth <= 0 ||
            slotWidth > int.MaxValue ||
            (long)chevron.Left - taskbar.Left < slotWidth)
        {
            return false;
        }

        var left = (long)chevron.Left - slotWidth;
        if (left < int.MinValue || left > int.MaxValue)
        {
            return false;
        }

        anchor = new TaskbarAnchor(
            (int)left,
            taskbar.Top,
            (int)slotWidth,
            taskbar.Height);
        return true;
    }
}
