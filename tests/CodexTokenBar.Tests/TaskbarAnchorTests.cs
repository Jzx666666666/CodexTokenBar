using CodexTokenBar.Taskbar;

namespace CodexTokenBar.Tests;

internal static class TaskbarAnchorTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("TaskbarAnchorCalculator_96Dpi_UsesChevronAsRightEdge", async () =>
        {
            var primaryScreen = new PixelRect(0, 0, 1920, 1080);
            var taskbar = new PixelRect(0, 1032, 1920, 48);
            var chevron = new PixelRect(1820, 1032, 24, 48);

            var calculated = TaskbarAnchorCalculator.TryCalculate(
                primaryScreen,
                taskbar,
                chevron,
                96,
                out var anchor);

            Assert.Equal(true, calculated);
            Assert.Equal(new TaskbarAnchor(1756, 1032, 64, 48), anchor);
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_144Dpi_Uses64DipWidth", async () =>
        {
            var primaryScreen = new PixelRect(0, 0, 2880, 1620);
            var taskbar = new PixelRect(0, 1548, 2880, 72);
            var chevron = new PixelRect(2700, 1548, 24, 72);

            var calculated = TaskbarAnchorCalculator.TryCalculate(
                primaryScreen,
                taskbar,
                chevron,
                144,
                out var anchor);

            Assert.Equal(true, calculated);
            Assert.Equal(new TaskbarAnchor(2604, 1548, 96, 72), anchor);
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_192Dpi_Uses64DipWidth", async () =>
        {
            var primaryScreen = new PixelRect(0, 0, 3840, 2160);
            var taskbar = new PixelRect(0, 2040, 3840, 120);
            var chevron = new PixelRect(3600, 2040, 24, 120);

            var calculated = TaskbarAnchorCalculator.TryCalculate(
                primaryScreen,
                taskbar,
                chevron,
                192,
                out var anchor);

            Assert.Equal(true, calculated);
            Assert.Equal(new TaskbarAnchor(3472, 2040, 128, 120), anchor);
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_NegativeScreenCoordinatesRemainValid", async () =>
        {
            var primaryScreen = new PixelRect(-1920, -120, 1920, 1080);
            var taskbar = new PixelRect(-1920, 912, 1920, 48);
            var chevron = new PixelRect(-100, 912, 24, 48);

            var calculated = TaskbarAnchorCalculator.TryCalculate(
                primaryScreen,
                taskbar,
                chevron,
                96,
                out var anchor);

            Assert.Equal(true, calculated);
            Assert.Equal(new TaskbarAnchor(-164, 912, 64, 48), anchor);
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_RejectsChevronOutsideTaskbar", async () =>
        {
            var calculated = TaskbarAnchorCalculator.TryCalculate(
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 1032, 1920, 48),
                new PixelRect(1930, 1032, 24, 48),
                96,
                out _);

            Assert.Equal(false, calculated);
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_RejectsNonBottomTaskbar", async () =>
        {
            var calculated = TaskbarAnchorCalculator.TryCalculate(
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 1000, 1920, 48),
                new PixelRect(1820, 1000, 24, 48),
                96,
                out _);

            Assert.Equal(false, calculated);
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_RejectsSlotTooSmall", async () =>
        {
            var calculated = TaskbarAnchorCalculator.TryCalculate(
                new PixelRect(0, 0, 1920, 1080),
                new PixelRect(0, 1032, 1920, 48),
                new PixelRect(40, 1032, 24, 48),
                96,
                out _);

            Assert.Equal(false, calculated);
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_RejectsNonPositiveDpi", async () =>
        {
            var primaryScreen = new PixelRect(0, 0, 1920, 1080);
            var taskbar = new PixelRect(0, 1032, 1920, 48);
            var chevron = new PixelRect(1820, 1032, 24, 48);

            Assert.Equal(
                false,
                TaskbarAnchorCalculator.TryCalculate(primaryScreen, taskbar, chevron, 0, out _));
            Assert.Equal(
                false,
                TaskbarAnchorCalculator.TryCalculate(primaryScreen, taskbar, chevron, -1, out _));
            await Task.CompletedTask;
        }),
        new("TaskbarAnchorCalculator_RejectsInvalidRectangles", async () =>
        {
            var taskbar = new PixelRect(0, 1032, 1920, 48);
            var chevron = new PixelRect(1820, 1032, 24, 48);

            Assert.Equal(
                false,
                TaskbarAnchorCalculator.TryCalculate(
                    new PixelRect(0, 0, 0, 1080), taskbar, chevron, 96, out _));
            Assert.Equal(
                false,
                TaskbarAnchorCalculator.TryCalculate(
                    new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 1032, 1920, 0), chevron, 96, out _));
            Assert.Equal(
                false,
                TaskbarAnchorCalculator.TryCalculate(
                    new PixelRect(0, 0, 1920, 1080), taskbar, new PixelRect(1820, 1032, 0, 48), 96, out _));
            await Task.CompletedTask;
        }),
    ];
}
