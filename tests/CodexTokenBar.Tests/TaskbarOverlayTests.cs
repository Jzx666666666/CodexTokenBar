using CodexTokenBar.Domain;
using CodexTokenBar.Taskbar;
using CodexTokenBar.UI;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexTokenBar.Tests;

internal static class TaskbarOverlayTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("TaskbarOverlay.ValidProbeShowsAndRepositions", ValidProbeShowsAndRepositions),
        new("TaskbarOverlay.FailedProbeHidesOverlay", FailedProbeHidesOverlay),
        new("TaskbarOverlay.SetAnchorFailureRestoresFallback", SetAnchorFailureRestoresFallback),
        new("TaskbarOverlay.RecoveryShowsOverlayAgain", RecoveryShowsOverlayAgain),
        new("TaskbarOverlay.AvailabilityEventOnlyFiresOnTransitions", AvailabilityEventOnlyFiresOnTransitions),
        new("TaskbarOverlay.StartUsesOnlyOnePositionLoop", StartUsesOnlyOnePositionLoop),
        new("TaskbarOverlay.StopIsIdempotentAndCleansUp", StopIsIdempotentAndCleansUp),
        new("TaskbarOverlay.UpdatePublishesFreshAndStaleState", UpdatePublishesFreshAndStaleState),
        new("TaskbarOverlay.SingleClickRunsAfterDoubleClickInterval", SingleClickRunsAfterDoubleClickInterval),
        new("TaskbarOverlay.DoubleClickCancelsDelayedSingleClick", DoubleClickCancelsDelayedSingleClick),
        new("TaskbarOverlay.RightClickIsImmediate", RightClickIsImmediate),
        new("TaskbarOverlay.WindowEventsReachCoordinator", WindowEventsReachCoordinator),
        new("TaskbarOverlay.PhysicalPlacementPreservesMainMonitorPixels", PhysicalPlacementPreservesMainMonitorPixels),
        new("TaskbarOverlay.PhysicalPlacementPreservesNegativeCoordinates", PhysicalPlacementPreservesNegativeCoordinates),
        new("TaskbarOverlay.PlacementCommandRequestsTopmostWithoutActivation", PlacementCommandRequestsTopmostWithoutActivation),
        new("TaskbarPresentation_Fresh83UsesStandardTextAndFontSize", Fresh83UsesStandardTextAndFontSize),
        new("TaskbarPresentation_Fresh100UsesCompactFontSize", Fresh100UsesCompactFontSize),
        new("TaskbarPresentation_StaleUsesEmDash", StaleUsesEmDash),
        new("TaskbarPresentation_UsesApprovedTypographyConstants", UsesApprovedTypographyConstants),
        new("TaskbarPresentation_FontSizesAreMonotonicAndClamped", FontSizesAreMonotonicAndClamped),
        new("TaskbarPresentation_LightAndDarkTaskbarsSelectContrastingTone", LightAndDarkTaskbarsSelectContrastingTone),
        new("TaskbarOverlay_ReassertTopmostUsesPhysicalNoActivateContract", ReassertTopmostUsesPhysicalNoActivateContract),
        new("TaskbarPresentation_WpfWindowAt144DpiAppliesMetricsAndReassertsWithoutMutation", WpfWindowAt144DpiAppliesMetricsAndReassertsWithoutMutation),
        new("TaskbarOverlay.SummaryPlacementUsesAnchorTopAndEightDipGap", SummaryPlacementUsesAnchorTopAndEightDipGap),
        new("TaskbarOverlay.SummaryPlacementClampsToPrimaryWorkArea", SummaryPlacementClampsToPrimaryWorkArea),
        new("TaskbarOverlay.AvailabilityHidesNotifyIconAndFallbackRestoresIt", AvailabilityHidesNotifyIconAndFallbackRestoresIt),
    ];

    private static Task ValidProbeShowsAndRepositions()
    {
        var first = Anchor(1756, 1032, 64, 48);
        var second = Anchor(1748, 1032, 64, 48);
        var probe = new FakeProbe(first, second);
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);

        coordinator.Start();
        loop.Pulse();

        Assert.Equal(true, coordinator.IsOverlayAvailable);
        Assert.Equal(2, window.ShowCount);
        Assert.SequenceEqual(new[] { first, second }, window.Anchors);
        return Task.CompletedTask;
    }

    private static Task FailedProbeHidesOverlay()
    {
        var probe = new FakeProbe((TaskbarAnchor?)null);
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);

        coordinator.Start();

        Assert.Equal(false, coordinator.IsOverlayAvailable);
        Assert.Equal(1, window.HideCount);
        Assert.Equal(0, window.ShowCount);
        return Task.CompletedTask;
    }

    private static Task SetAnchorFailureRestoresFallback()
    {
        var renderable = new TaskbarAnchor(1756, 1032, 64, 48);
        var tooSmallAt144Dpi = new TaskbarAnchor(2500, 1300, 96, 30);
        var probe = new FakeProbe(renderable, tooSmallAt144Dpi);
        var window = new FakeOverlayWindow
        {
            SetAnchorFailure = anchor => anchor == tooSmallAt144Dpi
                ? new InvalidOperationException("The taskbar is too short for a 24 DIP quota ring.")
                : null,
        };
        var loop = new ManualPositionLoop();
        var notifyIconVisible = true;
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);
        coordinator.NotifyIconVisibilityChanged += visible => notifyIconVisible = visible;

        coordinator.Start();
        Assert.Equal(true, coordinator.IsOverlayAvailable);
        Assert.Equal(false, notifyIconVisible);

        loop.Pulse();

        Assert.Equal(false, coordinator.IsOverlayAvailable);
        Assert.Equal(true, notifyIconVisible);
        Assert.Equal(1, window.HideCount);
        Assert.Equal(1, window.ShowCount);
        return Task.CompletedTask;
    }

    private static Task RecoveryShowsOverlayAgain()
    {
        var anchor = Anchor(1756, 1032, 64, 48);
        var probe = new FakeProbe(null, anchor);
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);

        coordinator.Start();
        loop.Pulse();

        Assert.Equal(true, coordinator.IsOverlayAvailable);
        Assert.Equal(1, window.ShowCount);
        return Task.CompletedTask;
    }

    private static Task AvailabilityEventOnlyFiresOnTransitions()
    {
        var anchor = Anchor(1756, 1032, 64, 48);
        var probe = new FakeProbe(anchor, anchor, null, null, anchor);
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);
        var events = new List<bool>();
        coordinator.OverlayAvailabilityChanged += available => events.Add(available);

        coordinator.Start();
        loop.Pulse();
        loop.Pulse();
        loop.Pulse();
        loop.Pulse();

        Assert.SequenceEqual(new[] { true, false, true }, events);
        return Task.CompletedTask;
    }

    private static Task StartUsesOnlyOnePositionLoop()
    {
        var probe = new FakeProbe(Anchor(1756, 1032, 64, 48));
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);

        coordinator.Start();
        coordinator.Start();

        Assert.Equal(1, loop.StartCount);
        return Task.CompletedTask;
    }

    private static Task StopIsIdempotentAndCleansUp()
    {
        var probe = new FakeProbe(Anchor(1756, 1032, 64, 48));
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);

        coordinator.Start();
        coordinator.Stop();
        coordinator.Stop();

        Assert.Equal(1, loop.StopCount);
        Assert.Equal(1, window.HideCount);
        Assert.Equal(1, window.CloseCount);
        Assert.Equal(false, coordinator.IsOverlayAvailable);
        coordinator.Dispose();
        return Task.CompletedTask;
    }

    private static Task UpdatePublishesFreshAndStaleState()
    {
        var probe = new FakeProbe(Anchor(1756, 1032, 64, 48));
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);
        var fresh = State(UsageFreshness.Fresh, 47);
        var stale = State(UsageFreshness.Stale, 47);

        coordinator.Start();
        coordinator.Update(fresh);
        coordinator.Update(stale);

        Assert.SequenceEqual(new[] { fresh, stale }, window.States);
        return Task.CompletedTask;
    }

    private static Task SingleClickRunsAfterDoubleClickInterval()
    {
        var delay = new ManualClickDelay();
        var interaction = new TaskbarClickSequencer(delay, TimeSpan.FromMilliseconds(250));
        var singleClicks = 0;
        interaction.SingleLeftClick += () => singleClicks++;

        interaction.OnLeftClick();
        Assert.Equal(0, singleClicks);
        delay.Fire();

        Assert.Equal(1, singleClicks);
        Assert.Equal(1, delay.ScheduleCount);
        return Task.CompletedTask;
    }

    private static Task DoubleClickCancelsDelayedSingleClick()
    {
        var delay = new ManualClickDelay();
        var interaction = new TaskbarClickSequencer(delay, TimeSpan.FromMilliseconds(250));
        var singleClicks = 0;
        interaction.SingleLeftClick += () => singleClicks++;

        interaction.OnLeftClick();
        interaction.OnDoubleClick();
        delay.Fire();

        Assert.Equal(0, singleClicks);
        Assert.Equal(1, delay.CancelCount);
        return Task.CompletedTask;
    }

    private static Task RightClickIsImmediate()
    {
        var delay = new ManualClickDelay();
        var interaction = new TaskbarClickSequencer(delay, TimeSpan.FromMilliseconds(250));
        var rightClicks = 0;
        interaction.RightClickRequested += () => rightClicks++;

        interaction.OnRightClick();

        Assert.Equal(1, rightClicks);
        Assert.Equal(0, delay.ScheduleCount);
        return Task.CompletedTask;
    }

    private static Task WindowEventsReachCoordinator()
    {
        var probe = new FakeProbe(Anchor(1756, 1032, 64, 48));
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        var delay = new ManualClickDelay();
        using var coordinator = new TaskbarOverlayCoordinator(
            probe,
            window,
            loop,
            delay,
            TimeSpan.FromMilliseconds(250));
        var singles = 0;
        var rights = 0;
        coordinator.SingleLeftClick += () => singles++;
        coordinator.RightClickRequested += () => rights++;

        coordinator.Start();
        window.RaiseLeftClick();
        Assert.Equal(0, singles);
        delay.Fire();
        Assert.Equal(1, singles);

        window.RaiseRightClick();
        Assert.Equal(1, rights);

        window.RaiseLeftClick();
        window.RaiseDoubleClick();
        delay.Fire();
        Assert.Equal(1, singles);
        return Task.CompletedTask;
    }

    private static Task PhysicalPlacementPreservesMainMonitorPixels()
    {
        var anchor = new TaskbarAnchor(3058, 1392, 64, 48);

        var calculated = TaskbarWindowPlacementCalculator.TryFromAnchor(anchor, out var placement);

        Assert.Equal(true, calculated);
        Assert.Equal(anchor.PixelSourceRect, placement.PixelRect);
        Assert.Equal(64d, placement.WidthDip);
        Assert.Equal(48d, placement.HeightDip);
        return Task.CompletedTask;
    }

    private static Task PhysicalPlacementPreservesNegativeCoordinates()
    {
        var primary = new TaskbarAnchor(-64, 1000, 128, 96);
        var secondary = new TaskbarAnchor(3440, -387, 96, 72);

        Assert.Equal(true, TaskbarWindowPlacementCalculator.TryFromAnchor(primary, out var primaryPlacement));
        Assert.Equal(true, TaskbarWindowPlacementCalculator.TryFromAnchor(secondary, out var secondaryPlacement));
        Assert.Equal(primary.PixelSourceRect, primaryPlacement.PixelRect);
        Assert.Equal(secondary.PixelSourceRect, secondaryPlacement.PixelRect);
        return Task.CompletedTask;
    }

    private static Task PlacementCommandRequestsTopmostWithoutActivation()
    {
        var anchor = new TaskbarAnchor(3058, 1392, 64, 48);
        Assert.Equal(true, TaskbarWindowPlacementCalculator.TryFromAnchor(anchor, out var placement));

        var command = TaskbarWindowPlacementCalculator.CreateSetWindowPosCommand(
            placement,
            showWindow: true);

        Assert.Equal(true, command.RequestsTopmost);
        Assert.Equal(true, command.DoesNotActivate);
        Assert.Equal(true, command.PreservesOwnerZOrder);
        Assert.Equal(true, command.RequestsShow);
        Assert.Equal(true, command.DoesNotSuppressZOrder);
        Assert.Equal(anchor.PixelSourceRect, command.Placement.PixelRect);
        return Task.CompletedTask;
    }

    private static Task Fresh83UsesStandardTextAndFontSize()
    {
        Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(48, 96, out var metrics));

        var presentation = TaskbarQuotaPresentation.Create(State(UsageFreshness.Fresh, 83), metrics, useLightText: false);

        Assert.Equal("83%", presentation.Text);
        Assert.Equal(metrics.FontSizeDip, presentation.FontSizeDip);
        Assert.Equal(TaskbarTextTone.Dark, presentation.Tone);
        return Task.CompletedTask;
    }

    private static Task Fresh100UsesCompactFontSize()
    {
        Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(48, 96, out var metrics));

        var presentation = TaskbarQuotaPresentation.Create(State(UsageFreshness.Fresh, 100), metrics, useLightText: true);

        Assert.Equal("100%", presentation.Text);
        Assert.Equal(metrics.CompactFontSizeDip, presentation.FontSizeDip);
        Assert.Equal(TaskbarTextTone.Light, presentation.Tone);
        return Task.CompletedTask;
    }

    private static Task StaleUsesEmDash()
    {
        Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(48, 96, out var metrics));

        var presentation = TaskbarQuotaPresentation.Create(State(UsageFreshness.Stale, 83), metrics, useLightText: true);

        Assert.Equal("\u2014", presentation.Text);
        Assert.Equal(metrics.FontSizeDip, presentation.FontSizeDip);
        return Task.CompletedTask;
    }

    private static Task UsesApprovedTypographyConstants()
    {
        Assert.Equal("Segoe UI Variable Text, Segoe UI", TaskbarQuotaPresentation.FontFamilyName);
        Assert.Equal("SemiBold", TaskbarQuotaPresentation.FontWeightName);
        Assert.Equal(TaskbarTextTone.Light, (TaskbarTextTone)Enum.Parse(typeof(TaskbarTextTone), "Light"));
        Assert.Equal(TaskbarTextTone.Dark, (TaskbarTextTone)Enum.Parse(typeof(TaskbarTextTone), "Dark"));
        return Task.CompletedTask;
    }

    private static Task FontSizesAreMonotonicAndClamped()
    {
        Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(32, 96, out var smallMetrics));
        Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(40, 96, out var mediumMetrics));
        Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(48, 96, out var largeMetrics));

        var small = TaskbarQuotaPresentation.Create(State(UsageFreshness.Fresh, 83), smallMetrics, useLightText: false);
        var medium = TaskbarQuotaPresentation.Create(State(UsageFreshness.Fresh, 83), mediumMetrics, useLightText: false);
        var large = TaskbarQuotaPresentation.Create(State(UsageFreshness.Fresh, 83), largeMetrics, useLightText: false);

        Require(small.FontSizeDip < medium.FontSizeDip && medium.FontSizeDip < large.FontSizeDip,
            "standard text sizes must increase with the adaptive ring diameter.");
        Require(small.FontSizeDip <= 12d && medium.FontSizeDip <= 12d && large.FontSizeDip <= 12d,
            "standard text sizes must remain clamped.");
        return Task.CompletedTask;
    }

    private static Task LightAndDarkTaskbarsSelectContrastingTone()
    {
        Assert.Equal(true, TaskbarVisualMetricsCalculator.TryCalculate(48, 96, out var metrics));

        var lightText = TaskbarQuotaPresentation.Create(State(UsageFreshness.Fresh, 83), metrics, useLightText: true);
        var darkText = TaskbarQuotaPresentation.Create(State(UsageFreshness.Fresh, 83), metrics, useLightText: false);

        Assert.Equal(TaskbarTextTone.Light, lightText.Tone);
        Assert.Equal(TaskbarTextTone.Dark, darkText.Tone);
        return Task.CompletedTask;
    }

    private static Task ReassertTopmostUsesPhysicalNoActivateContract()
    {
        var method = typeof(TaskbarQuotaWindow).GetMethod("ReassertTopmost");
        Require(method is not null && method.ReturnType == typeof(void) && method.GetParameters().Length == 0,
            "TaskbarQuotaWindow must expose a parameterless ReassertTopmost contract.");

        var anchor = new TaskbarAnchor(3058, 1392, 64, 48);
        Assert.Equal(true, TaskbarWindowPlacementCalculator.TryFromAnchor(anchor, out var placement));
        var command = TaskbarWindowPlacementCalculator.CreateSetWindowPosCommand(placement, showWindow: false);
        Assert.Equal(true, command.RequestsTopmost);
        Assert.Equal(true, command.DoesNotActivate);
        Assert.Equal(false, command.RequestsShow);
        Assert.Equal(anchor.PixelSourceRect, command.Placement.PixelRect);
        return Task.CompletedTask;
    }

    private static Task WpfWindowAt144DpiAppliesMetricsAndReassertsWithoutMutation() => RunOnSta(() =>
    {
        TaskbarQuotaWindow? window = null;
        try
        {
            window = new TaskbarQuotaWindow();
            var anchor = new TaskbarAnchor(2500, 1300, 96, 60);
            window.SetAnchor(anchor);
            window.Update(State(UsageFreshness.Fresh, 83));

            var ringImage = (Image?)window.FindName("RingImage")
                ?? throw new InvalidOperationException("RingImage was not created from XAML.");
            var ringText = (TextBlock?)window.FindName("RingText")
                ?? throw new InvalidOperationException("RingText was not created from XAML.");
            var surface = (Border?)window.FindName("Surface")
                ?? throw new InvalidOperationException("Surface was not created from XAML.");
            var source = ringImage.Source as BitmapSource
                ?? throw new InvalidOperationException("RingImage.Source was not a BitmapSource.");

            Assert.Equal(144, anchor.Dpi);
            Assert.Equal(32d, ringImage.Width);
            Assert.Equal(32d, ringImage.Height);
            Assert.Equal(48, source.PixelWidth);
            Assert.Equal(48, source.PixelHeight);
            Assert.Equal("83%", ringText.Text);
            Assert.Equal(9.6d, ringText.FontSize);
            Assert.Equal("Segoe UI Variable Text, Segoe UI", ringText.FontFamily.Source);
            Assert.Equal(FontWeights.SemiBold, ringText.FontWeight);
            Assert.Equal(TextAlignment.Center, ringText.TextAlignment);
            Assert.Equal(HorizontalAlignment.Center, ringText.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, ringText.VerticalAlignment);
            Assert.Equal(new CornerRadius(3.2d), surface.CornerRadius);

            var hwndBefore = new WindowInteropHelper(window).Handle;
            var visibleBefore = window.IsVisible;
            var foregroundBefore = GetForegroundWindow();
            var sourceBefore = ringImage.Source;
            var textBefore = ringText.Text;

            Require(hwndBefore != IntPtr.Zero, "SetAnchor must create one real HWND.");
            Assert.Equal(false, visibleBefore);

            window.ReassertTopmost();

            Assert.Equal(hwndBefore, new WindowInteropHelper(window).Handle);
            Assert.Equal(visibleBefore, window.IsVisible);
            Assert.Equal(foregroundBefore, GetForegroundWindow());
            Require(ReferenceEquals(sourceBefore, ringImage.Source),
                "ReassertTopmost must not rerender or replace RingImage.Source.");
            Assert.Equal(textBefore, ringText.Text);
        }
        finally
        {
            window?.Close();
        }
    });

    private static Task SummaryPlacementUsesAnchorTopAndEightDipGap()
    {
        var anchor = new TaskbarAnchor(3058, 1392, 64, 48);
        var workArea = new PixelRect(0, 0, 3440, 1392);
        const double summaryHeight = 430d;

        var calculated = SummaryWindowPlacementCalculator.TryCalculate(
            anchor,
            workArea,
            summaryWidthDip: 404d,
            summaryHeightDip: summaryHeight,
            out var placement);

        Assert.Equal(true, calculated);
        Assert.Equal(954d, placement.Top);
        Assert.Equal(8d, anchor.Top * 96d / anchor.Dpi - placement.Top - summaryHeight);
        Assert.Equal(true, placement.Left >= 0d);
        Assert.Equal(true, placement.Left + 404d <= 3440d);
        return Task.CompletedTask;
    }

    private static Task SummaryPlacementClampsToPrimaryWorkArea()
    {
        var anchor = new TaskbarAnchor(3058, 1392, 64, 48);
        var workArea = new PixelRect(0, 0, 3440, 1392);

        var calculated = SummaryWindowPlacementCalculator.TryCalculate(
            anchor,
            workArea,
            summaryWidthDip: 404d,
            summaryHeightDip: 1400d,
            out var placement);

        Assert.Equal(true, calculated);
        Assert.Equal(0d, placement.Top);
        return Task.CompletedTask;
    }

    private static Task AvailabilityHidesNotifyIconAndFallbackRestoresIt()
    {
        var anchor = Anchor(1756, 1032, 64, 48);
        var probe = new FakeProbe(anchor, null, anchor);
        var window = new FakeOverlayWindow();
        var loop = new ManualPositionLoop();
        var notifyIconVisible = true;
        using var coordinator = new TaskbarOverlayCoordinator(probe, window, loop);
        coordinator.NotifyIconVisibilityChanged += visible => notifyIconVisible = visible;

        coordinator.Start();
        Assert.Equal(false, notifyIconVisible);
        loop.Pulse();
        Assert.Equal(true, notifyIconVisible);
        loop.Pulse();
        Assert.Equal(false, notifyIconVisible);
        coordinator.Stop();
        Assert.Equal(true, notifyIconVisible);
        return Task.CompletedTask;
    }

    private static TaskbarAnchor Anchor(int left, int top, int width, int height) =>
        new(left, top, width, height);

    private static UsageState State(UsageFreshness freshness, int remaining) => new(
        freshness,
        new QuotaView("codex", "Codex", remaining, DateTimeOffset.UtcNow.AddDays(7)),
        Array.Empty<QuotaView>(),
        DateTimeOffset.UtcNow,
        freshness == UsageFreshness.Stale ? "stale" : null,
        null);

    private sealed class FakeProbe(params TaskbarAnchor?[] results) : ITaskbarAnchorProbe
    {
        private readonly Queue<TaskbarAnchor?> _results = new(results);

        public bool TryGetAnchor(out TaskbarAnchor anchor)
        {
            var result = _results.Count == 0 ? null : _results.Dequeue();
            if (result is { } value)
            {
                anchor = value;
                return true;
            }

            anchor = null!;
            return false;
        }
    }

    private sealed class FakeOverlayWindow : ITaskbarOverlayWindow
    {
        public event Action? LeftClick;
        public event Action? DoubleClick;
        public event Action? RightClickRequested;

        public int ShowCount { get; private set; }
        public int HideCount { get; private set; }
        public int ReassertTopmostCount { get; private set; }
        public int CloseCount { get; private set; }
        public List<TaskbarAnchor> Anchors { get; } = [];
        public List<UsageState> States { get; } = [];
        public Func<TaskbarAnchor, Exception?>? SetAnchorFailure { get; init; }

        public void SetAnchor(TaskbarAnchor anchor)
        {
            Anchors.Add(anchor);
            if (SetAnchorFailure?.Invoke(anchor) is { } exception)
                throw exception;
        }
        public void Show() => ShowCount++;
        public void Hide() => HideCount++;
        public void ReassertTopmost() => ReassertTopmostCount++;
        public void Update(UsageState state) => States.Add(state);
        public void Close() => CloseCount++;
        public void RaiseLeftClick() => LeftClick?.Invoke();
        public void RaiseDoubleClick() => DoubleClick?.Invoke();
        public void RaiseRightClick() => RightClickRequested?.Invoke();
    }

    private static Task RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("The WPF STA integration test did not finish within 15 seconds.");
        if (failure is not null)
            throw new InvalidOperationException("The WPF STA integration test failed.", failure);
        return Task.CompletedTask;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class ManualPositionLoop : ITaskbarPositionLoop
    {
        private Action? _tick;

        public int StartCount { get; private set; }
        public int StopCount { get; private set; }

        public void Start(Action tick)
        {
            StartCount++;
            _tick = tick;
        }

        public void Stop()
        {
            StopCount++;
            _tick = null;
        }

        public void Pulse() => _tick?.Invoke();
    }

    private sealed class ManualClickDelay : ITaskbarClickDelay
    {
        private Action? _callback;

        public int ScheduleCount { get; private set; }
        public int CancelCount { get; private set; }

        public void Schedule(TimeSpan delay, Action callback)
        {
            ScheduleCount++;
            _callback = callback;
        }

        public void Cancel()
        {
            CancelCount++;
            _callback = null;
        }

        public void Fire()
        {
            var callback = _callback;
            _callback = null;
            callback?.Invoke();
        }
    }
}
