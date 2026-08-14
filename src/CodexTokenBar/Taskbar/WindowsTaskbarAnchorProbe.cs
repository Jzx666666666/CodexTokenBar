using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CodexTokenBar.Taskbar;

public readonly record struct TaskbarChevronCandidate(
    PixelRect Bounds,
    string? Name,
    string? AutomationId,
    bool IsButton,
    bool IsVisible);

public readonly record struct TaskbarNativeGeometry(
    PixelRect PrimaryScreen,
    PixelRect Taskbar,
    PixelRect? NotificationArea,
    int Dpi);

public interface ITaskbarNativeGeometryProvider
{
    bool TryGetGeometry(out TaskbarNativeGeometry geometry);
}

public interface ITaskbarUiAutomationProvider
{
    bool TryFindChevron(
        PixelRect taskbar,
        PixelRect? notificationArea,
        int dpiTolerancePixels,
        out PixelRect chevron);
}

public static class TaskbarChevronSelector
{
    public const string SystemTrayIconAutomationId = "SystemTrayIcon";

    private static readonly string[] ChevronNames =
    [
        "Show hidden icons",
        "\u663e\u793a\u9690\u85cf\u7684\u56fe\u6807",
        "\u663e\u793a\u9690\u85cf\u56fe\u6807",
    ];

    public static bool TrySelect(
        IReadOnlyList<TaskbarChevronCandidate> candidates,
        PixelRect primaryTaskbar,
        PixelRect? notificationArea,
        int dpiTolerancePixels,
        out PixelRect chevron)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        chevron = default;
        if (!primaryTaskbar.IsValid || dpiTolerancePixels < 0)
            return false;

        var eligible = candidates
            .Where(candidate =>
                candidate.IsButton &&
                candidate.IsVisible &&
                candidate.Bounds.IsValid &&
                primaryTaskbar.Contains(candidate.Bounds))
            .ToArray();

        var named = eligible
            .Where(candidate => IsChevronName(candidate.Name))
            .OrderBy(candidate => candidate.Bounds.Left)
            .ThenBy(candidate => candidate.Bounds.Top)
            .ToArray();
        if (named.Length > 0)
        {
            chevron = named[0].Bounds;
            return true;
        }

        if (notificationArea is not { } area ||
            !primaryTaskbar.Contains(area) ||
            !area.IsValid)
        {
            return false;
        }

        var idCandidate = eligible
            .Where(candidate =>
                string.Equals(
                    candidate.AutomationId,
                    SystemTrayIconAutomationId,
                    StringComparison.OrdinalIgnoreCase) &&
                area.Contains(candidate.Bounds) &&
                Math.Abs((long)candidate.Bounds.Left - area.Left) <= dpiTolerancePixels)
            .OrderBy(candidate => candidate.Bounds.Left)
            .ThenBy(candidate => candidate.Bounds.Top)
            .FirstOrDefault();
        if (idCandidate.Bounds.IsValid)
        {
            chevron = idCandidate.Bounds;
            return true;
        }

        return false;
    }

    public static bool IsChevronName(string? value) =>
        value is not null &&
        ChevronNames.Any(name => string.Equals(value.Trim(), name, StringComparison.OrdinalIgnoreCase));
}

public sealed class WindowsTaskbarAnchorProbe : ITaskbarAnchorProbe
{
    private readonly ITaskbarNativeGeometryProvider _nativeGeometry;
    private readonly ITaskbarUiAutomationProvider _uiAutomation;

    public WindowsTaskbarAnchorProbe()
        : this(new WindowsTaskbarNativeGeometryProvider(), new WindowsTaskbarUiAutomationProvider())
    {
    }

    public WindowsTaskbarAnchorProbe(
        ITaskbarNativeGeometryProvider nativeGeometry,
        ITaskbarUiAutomationProvider uiAutomation)
    {
        _nativeGeometry = nativeGeometry ?? throw new ArgumentNullException(nameof(nativeGeometry));
        _uiAutomation = uiAutomation ?? throw new ArgumentNullException(nameof(uiAutomation));
    }

    public bool TryGetAnchor(out TaskbarAnchor anchor)
    {
        anchor = null!;
        try
        {
            if (!_nativeGeometry.TryGetGeometry(out var native))
                return false;

            if (native.NotificationArea is { } notificationArea)
            {
                // The notification boundary is the only trusted native seam. It
                // supplies the chevron's left edge without guessing child icons.
                return TaskbarAnchorCalculator.TryCalculateFromNotificationBoundary(
                    native.PrimaryScreen,
                    native.Taskbar,
                    notificationArea,
                    native.Dpi,
                    out anchor);
            }

            // UI Automation is only a bounded fallback when TrayNotifyWnd is
            // genuinely unavailable. It is never consulted on the native path.
            if (!_uiAutomation.TryFindChevron(
                    native.Taskbar,
                    notificationArea: null,
                    GetDpiTolerancePixels(native.Dpi),
                    out var chevron))
            {
                return false;
            }

            return TaskbarAnchorCalculator.TryCalculate(
                native.PrimaryScreen,
                native.Taskbar,
                chevron,
                native.Dpi,
                out anchor);
        }
        catch (ElementNotAvailableException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (COMException)
        {
            return false;
        }
        catch (ExternalException)
        {
            return false;
        }
    }

    private static int GetDpiTolerancePixels(int dpi) =>
        Math.Max(1, (int)Math.Round(dpi / 96d, MidpointRounding.AwayFromZero) - 1);
}

public sealed class WindowsTaskbarNativeGeometryProvider : ITaskbarNativeGeometryProvider
{
    public bool TryGetGeometry(out TaskbarNativeGeometry geometry)
    {
        geometry = default;

        var primary = Screen.PrimaryScreen;
        if (primary is null)
            return false;

        var taskbarHandle = FindWindow("Shell_TrayWnd", null);
        if (taskbarHandle == IntPtr.Zero || !GetWindowRect(taskbarHandle, out var taskbarRect))
            return false;

        var primaryRect = new PixelRect(
            primary.Bounds.Left,
            primary.Bounds.Top,
            primary.Bounds.Width,
            primary.Bounds.Height);
        var taskbar = ToPixelRect(taskbarRect);
        if (!primaryRect.IsValid || !taskbar.IsValid)
            return false;

        var dpi = checked((int)GetDpiForWindow(taskbarHandle));
        if (dpi <= 0)
            dpi = 96;

        PixelRect? notificationArea = null;
        var notificationHandle = FindWindowEx(
            taskbarHandle,
            IntPtr.Zero,
            "TrayNotifyWnd",
            null);
        if (notificationHandle != IntPtr.Zero)
        {
            // Preserve an invalid, present boundary so the caller rejects it
            // instead of silently switching to an untrusted guessed position.
            notificationArea = GetWindowRect(notificationHandle, out var notificationRect)
                ? ToPixelRect(notificationRect)
                : new PixelRect(0, 0, 0, 0);
        }

        geometry = new TaskbarNativeGeometry(primaryRect, taskbar, notificationArea, dpi);
        return true;
    }

    private static PixelRect ToPixelRect(RECT rectangle) =>
        new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right - rectangle.Left,
            rectangle.Bottom - rectangle.Top);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindowEx(
        IntPtr parentHandle,
        IntPtr childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out RECT rectangle);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct RECT
    {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}

public sealed class WindowsTaskbarUiAutomationProvider : ITaskbarUiAutomationProvider
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMilliseconds(200);

    private readonly TimeSpan _timeout;
    private int _inFlight;

    public WindowsTaskbarUiAutomationProvider(TimeSpan? timeout = null)
    {
        _timeout = timeout ?? DefaultTimeout;
        if (_timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
    }

    public bool TryFindChevron(
        PixelRect taskbar,
        PixelRect? notificationArea,
        int dpiTolerancePixels,
        out PixelRect chevron)
    {
        chevron = default;
        if (Interlocked.CompareExchange(ref _inFlight, 1, 0) != 0)
            return false;

        var task = Task.Run<(bool found, PixelRect result)>(() =>
        {
            try
            {
                var found = TryFindChevronCore(
                    taskbar,
                    notificationArea,
                    dpiTolerancePixels,
                    out var result);
                return (found: found, result: result);
            }
            catch (ElementNotAvailableException)
            {
                return (found: false, result: default);
            }
            catch (InvalidOperationException)
            {
                return (found: false, result: default);
            }
            catch (COMException)
            {
                return (found: false, result: default);
            }
            catch (ExternalException)
            {
                return (found: false, result: default);
            }
            finally
            {
                Volatile.Write(ref _inFlight, 0);
            }
        });

        try
        {
            if (!task.Wait(_timeout) || task.Status != TaskStatus.RanToCompletion)
                return false;

            var result = task.Result;
            if (result.found)
                chevron = result.result;
            return result.found;
        }
        catch (AggregateException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryFindChevronCore(
        PixelRect taskbar,
        PixelRect? notificationArea,
        int dpiTolerancePixels,
        out PixelRect chevron)
    {
        chevron = default;
        var root = AutomationElement.RootElement;
        if (root is null)
            return false;

        var nameConditions = new[]
        {
            new PropertyCondition(
                AutomationElement.NameProperty,
                "Show hidden icons",
                PropertyConditionFlags.IgnoreCase),
            new PropertyCondition(
                AutomationElement.NameProperty,
                "\u663e\u793a\u9690\u85cf\u7684\u56fe\u6807",
                PropertyConditionFlags.IgnoreCase),
            new PropertyCondition(
                AutomationElement.NameProperty,
                "\u663e\u793a\u9690\u85cf\u56fe\u6807",
                PropertyConditionFlags.IgnoreCase),
        };
        var namedMatches = root.FindAll(
            TreeScope.Descendants,
            new OrCondition(nameConditions.Cast<Condition>().ToArray()));
        var namedCandidates = ReadCandidates(namedMatches);
        if (TaskbarChevronSelector.TrySelect(
                namedCandidates,
                taskbar,
                notificationArea,
                dpiTolerancePixels,
                out chevron))
        {
            return true;
        }

        var automationIdCondition = new PropertyCondition(
            AutomationElement.AutomationIdProperty,
            TaskbarChevronSelector.SystemTrayIconAutomationId,
            PropertyConditionFlags.IgnoreCase);
        var idMatches = root.FindAll(TreeScope.Descendants, automationIdCondition);
        return TaskbarChevronSelector.TrySelect(
            ReadCandidates(idMatches),
            taskbar,
            notificationArea,
            dpiTolerancePixels,
            out chevron);
    }

    private static IReadOnlyList<TaskbarChevronCandidate> ReadCandidates(
        AutomationElementCollection elements)
    {
        var candidates = new List<TaskbarChevronCandidate>(elements.Count);
        foreach (AutomationElement element in elements)
        {
            try
            {
                var current = element.Current;
                var rectangle = current.BoundingRectangle;
                if (double.IsNaN(rectangle.Left) ||
                    double.IsNaN(rectangle.Top) ||
                    double.IsNaN(rectangle.Width) ||
                    double.IsNaN(rectangle.Height) ||
                    rectangle.Width <= 0 ||
                    rectangle.Height <= 0)
                {
                    continue;
                }

                var bounds = new PixelRect(
                    checked((int)Math.Round(rectangle.Left)),
                    checked((int)Math.Round(rectangle.Top)),
                    checked((int)Math.Round(rectangle.Width)),
                    checked((int)Math.Round(rectangle.Height)));
                candidates.Add(new TaskbarChevronCandidate(
                    bounds,
                    current.Name,
                    current.AutomationId,
                    current.ControlType == ControlType.Button,
                    !current.IsOffscreen));
            }
            catch (ElementNotAvailableException)
            {
            }
            catch (InvalidOperationException)
            {
            }
        }

        return candidates;
    }
}
