using CodexTokenBar.Taskbar;

namespace CodexTokenBar.Tests;

internal static class WindowsTaskbarProbeTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("WindowsTaskbarProbe.ExactAccessibleNamesHavePriority", ExactAccessibleNamesHavePriority),
        new("WindowsTaskbarProbe.SupportsEnglishAndChineseNameVariants", SupportsEnglishAndChineseNameVariants),
        new("WindowsTaskbarProbe.IdFallbackUsesNotificationBoundaryNotFirstCandidate", IdFallbackUsesNotificationBoundaryNotFirstCandidate),
        new("WindowsTaskbarProbe.IdFallbackRejectsMissingBoundary", IdFallbackRejectsMissingBoundary),
        new("WindowsTaskbarProbe.FiltersNonButtonInvisibleAndSecondaryCandidates", FiltersNonButtonInvisibleAndSecondaryCandidates),
        new("WindowsTaskbarProbe.NativeBoundaryUsesNotificationLeftAndSkipsUiAutomation", NativeBoundaryUsesNotificationLeftAndSkipsUiAutomation),
        new("WindowsTaskbarProbe.NativeBoundaryPureSeamUsesNotificationLeft", NativeBoundaryPureSeamUsesNotificationLeft),
        new("WindowsTaskbarProbe.NativeBoundaryRejectsInvalidGeometryAndInsufficientSlot", NativeBoundaryRejectsInvalidGeometryAndInsufficientSlot),
    ];

    private static Task ExactAccessibleNamesHavePriority()
    {
        var taskbar = MainTaskbar();
        var notification = NotificationArea();
        var expected = Chevron();
        var candidates = new[]
        {
            Candidate(3154, "Network", "SystemTrayIcon"),
            Candidate(expected.Left, "\u663e\u793a\u9690\u85cf\u7684\u56fe\u6807", "SystemTrayIcon"),
            Candidate(3190, "Volume", "SystemTrayIcon"),
        };

        var selected = TaskbarChevronSelector.TrySelect(
            candidates, taskbar, notification, 1, out var actual);

        Assert.Equal(true, selected);
        Assert.Equal(expected, actual);
        return Task.CompletedTask;
    }

    private static Task SupportsEnglishAndChineseNameVariants()
    {
        var names = new[]
        {
            "Show hidden icons",
            "\u663e\u793a\u9690\u85cf\u7684\u56fe\u6807",
            "\u663e\u793a\u9690\u85cf\u56fe\u6807",
        };

        foreach (var name in names)
        {
            var selected = TaskbarChevronSelector.TrySelect(
                [Candidate(Chevron().Left, name, "SystemTrayIcon")],
                MainTaskbar(),
                NotificationArea(),
                1,
                out var actual);

            Assert.Equal(true, selected);
            Assert.Equal(Chevron(), actual);
        }

        return Task.CompletedTask;
    }

    private static Task IdFallbackUsesNotificationBoundaryNotFirstCandidate()
    {
        var candidates = new[]
        {
            Candidate(3154, "Network", "SystemTrayIcon"),
            Candidate(3190, "Volume", "SystemTrayIcon"),
            Candidate(3122, "", "SystemTrayIcon"),
            Candidate(3260, "Time", "SystemTrayIcon"),
        };

        var selected = TaskbarChevronSelector.TrySelect(
            candidates, MainTaskbar(), NotificationArea(), 1, out var actual);

        Assert.Equal(true, selected);
        Assert.Equal(Chevron(), actual);
        return Task.CompletedTask;
    }

    private static Task IdFallbackRejectsMissingBoundary()
    {
        var candidates = new[]
        {
            Candidate(3154, "Network", "SystemTrayIcon"),
            Candidate(3190, "Volume", "SystemTrayIcon"),
            Candidate(3260, "Time", "SystemTrayIcon"),
        };

        var selected = TaskbarChevronSelector.TrySelect(
            candidates, MainTaskbar(), NotificationArea(), 1, out _);

        Assert.Equal(false, selected);
        return Task.CompletedTask;
    }

    private static Task FiltersNonButtonInvisibleAndSecondaryCandidates()
    {
        var candidates = new[]
        {
            new TaskbarChevronCandidate(Chevron(), "Show hidden icons", "SystemTrayIcon", false, true),
            new TaskbarChevronCandidate(Chevron(), "Show hidden icons", "SystemTrayIcon", true, false),
            new TaskbarChevronCandidate(
                new PixelRect(-1440, 1392, 32, 48),
                "Show hidden icons",
                "SystemTrayIcon",
                true,
                true),
        };

        var selected = TaskbarChevronSelector.TrySelect(
            candidates, MainTaskbar(), NotificationArea(), 1, out _);

        Assert.Equal(false, selected);
        return Task.CompletedTask;
    }

    private static Task NativeBoundaryUsesNotificationLeftAndSkipsUiAutomation()
    {
        var uiAutomation = new ThrowingUiAutomationProvider();
        var probe = new WindowsTaskbarAnchorProbe(
            new FakeNativeGeometryProvider(
                new TaskbarNativeGeometry(
                    new PixelRect(0, 0, 3440, 1440),
                    MainTaskbar(),
                    NotificationArea(),
                    96)),
            uiAutomation);

        var selected = probe.TryGetAnchor(out var anchor);

        Assert.Equal(true, selected);
        Assert.Equal(new TaskbarAnchor(3058, 1392, 64, 48), anchor);
        Assert.Equal(0, uiAutomation.CallCount);
        return Task.CompletedTask;
    }

    private static Task NativeBoundaryPureSeamUsesNotificationLeft()
    {
        var calculated = TaskbarAnchorCalculator.TryCalculateFromNotificationBoundary(
            new PixelRect(0, 0, 3440, 1440),
            MainTaskbar(),
            NotificationArea(),
            96,
            out var anchor);

        Assert.Equal(true, calculated);
        Assert.Equal(new TaskbarAnchor(3058, 1392, 64, 48), anchor);
        return Task.CompletedTask;
    }

    private static Task NativeBoundaryRejectsInvalidGeometryAndInsufficientSlot()
    {
        var primary = new PixelRect(0, 0, 3440, 1440);
        var taskbar = MainTaskbar();

        Assert.Equal(
            false,
            TaskbarAnchorCalculator.TryCalculateFromNotificationBoundary(
                primary,
                taskbar,
                new PixelRect(3122, 1392, 0, 48),
                96,
                out _));
        Assert.Equal(
            false,
            TaskbarAnchorCalculator.TryCalculateFromNotificationBoundary(
                primary,
                taskbar,
                new PixelRect(3122, 1392, 319, 48),
                96,
                out _));
        Assert.Equal(
            false,
            TaskbarAnchorCalculator.TryCalculateFromNotificationBoundary(
                primary,
                taskbar,
                new PixelRect(0, 1392, 318, 48),
                96,
                out _));
        return Task.CompletedTask;
    }

    private static TaskbarChevronCandidate Candidate(int left, string name, string automationId) =>
        new(new PixelRect(left, 1392, 32, 48), name, automationId, true, true);

    private static PixelRect MainTaskbar() => new(0, 1392, 3440, 48);

    private static PixelRect NotificationArea() => new(3122, 1392, 318, 48);

    private static PixelRect Chevron() => new(3122, 1392, 32, 48);

    private sealed class FakeNativeGeometryProvider(TaskbarNativeGeometry geometry)
        : ITaskbarNativeGeometryProvider
    {
        public bool TryGetGeometry(out TaskbarNativeGeometry actual)
        {
            actual = geometry;
            return true;
        }
    }

    private sealed class ThrowingUiAutomationProvider : ITaskbarUiAutomationProvider
    {
        public int CallCount { get; private set; }

        public bool TryFindChevron(
            PixelRect taskbar,
            PixelRect? notificationArea,
            int dpiTolerancePixels,
            out PixelRect chevron)
        {
            CallCount++;
            throw new InvalidOperationException("UI Automation must not run on the native fast path.");
        }
    }
}
