using CodexTokenBar.Codex;
using CodexTokenBar.Domain;
using CodexTokenBar.Lifecycle;
using CodexTokenBar.Monitoring;
using CodexTokenBar.Persistence;
using CodexTokenBar.Tray;
using CodexTokenBar.Taskbar;
using CodexTokenBar.UI;
using CodexTokenBar.Windows;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexTokenBar;

public partial class App : System.Windows.Application
{
    private ApplicationLifecycle? _lifecycle;
    private RollingLog? _log;
    private UsageMonitor? _monitor;
    private SummaryViewModel? _viewModel;
    private SummaryWindow? _window;
    private TrayIconController? _tray;
    private TaskbarOverlayCoordinator? _overlay;
    private PowerResumeWatcher? _resumeWatcher;
    private SingleInstanceCoordinator? _singleInstance;
    private AppSettings _settings = AppSettings.Default;
    private int _ownedProcessId;
    private int _shutdownRequested;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--probe", StringComparer.Ordinal))
        {
            ConsoleBridge.AttachToParent();
            var exitCode = await ProbeRunner.RunAsync(
                Console.Out,
                Console.Error,
                CancellationToken.None);
            Shutdown(exitCode);
            return;
        }

        if (e.Args.Contains("--design-preview", StringComparer.Ordinal))
        {
            await RunDesignPreviewAsync(e.Args);
            return;
        }

        try
        {
            await RunApplicationAsync();
        }
        catch (Exception exception)
        {
            await LogAsync($"startup-error={exception}");
            await ShutdownApplicationAsync(1);
        }
    }

    private async Task RunApplicationAsync()
    {
        var paths = new AppPaths();
        _log = new RollingLog(paths);
        var store = new AppStateStore(paths);
        _singleInstance = new SingleInstanceCoordinator();
        if (!await _singleInstance.TryAcquireAsync(CancellationToken.None))
        {
            try
            {
                await _singleInstance.SendShowAsync(CancellationToken.None);
            }
            finally
            {
                await _singleInstance.DisposeAsync();
            }
            Shutdown(0);
            return;
        }
        var startup = new StartupRegistration();
        async Task<IAppServerConnection> StartCodexConnectionAsync(CancellationToken token)
        {
            var commands = new CodexCommandLocator().FindAll();
            var connectionFactory = new CodexConnectionFactory(
                commands,
                diagnosticSink: (line, logToken) => _log.WriteAsync(line, logToken),
                processStarted: processId => Volatile.Write(ref _ownedProcessId, processId));
            return await connectionFactory.StartAsync(token);
        }
        var reader = new CodexRateLimitReader(StartCodexConnectionAsync);
        _monitor = new UsageMonitor(reader, store);
        _viewModel = new SummaryViewModel();
        _monitor.StateChanged += OnMonitorStateChanged;

        _lifecycle = new ApplicationLifecycle(new LifecycleActions
        {
            AcquireSingleInstanceAsync = _singleInstance.TryAcquireAsync,
            NotifyExistingInstanceAsync = _singleInstance.SendShowAsync,
            LoadSettingsAsync = async token => _settings = await store.LoadSettingsAsync(token),
            ApplyStartupAsync = token => IsStartupWriteDisabled()
                ? Task.CompletedTask
                : startup.ApplyAsync(_settings.StartWithWindows, GetExecutablePath(), token),
            CreateGrayTray = () =>
            {
                _window = new SummaryWindow(_viewModel);
                _tray = new TrayIconController(
                    _window, _monitor, startup, store, GetExecutablePath(), _settings);
                _overlay = new TaskbarOverlayCoordinator(
                    new WindowsTaskbarAnchorProbe(),
                    new TaskbarQuotaWindow(),
                    zOrderWatcher: new WindowsTaskbarZOrderWatcher());
                _tray.AttachOverlay(_overlay);
                _tray.ExitRequested += OnExitRequested;
                _tray.BackgroundError += OnBackgroundError;
                _resumeWatcher = new PowerResumeWatcher();
                _resumeWatcher.Resumed += OnResumed;
                _singleInstance.ActivationRequested += OnActivationRequested;
            },
            StartOverlay = () => _overlay?.Start(),
            StartMonitorAsync = async token =>
            {
                await _monitor.StartAsync(token);
                await WriteSmokeEvidenceAsync(_monitor.State);
            },
            StopMonitorAsync = _monitor.StopAsync,
            StopOverlay = () =>
            {
                _overlay?.Dispose();
                _overlay = null;
            },
            UnsubscribeResume = () =>
            {
                if (_resumeWatcher is not null)
                {
                    _resumeWatcher.Resumed -= OnResumed;
                    _resumeWatcher.Dispose();
                    _resumeWatcher = null;
                }
                _singleInstance.ActivationRequested -= OnActivationRequested;
                _monitor.StateChanged -= OnMonitorStateChanged;
            },
            DisposeTray = () =>
            {
                if (_tray is not null)
                {
                    _tray.ExitRequested -= OnExitRequested;
                    _tray.BackgroundError -= OnBackgroundError;
                    _tray.Dispose();
                    _tray = null;
                }
            },
            CloseWindow = () =>
            {
                _window?.CloseForExit();
                _window = null;
            },
            DisposeReaderAsync = _monitor.DisposeAsync,
            ReleaseSingleInstanceAsync = _singleInstance.DisposeAsync,
        });

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        var primary = await _lifecycle.StartAsync(CancellationToken.None);
        if (!primary)
            await ShutdownApplicationAsync(0);
    }

    private void OnMonitorStateChanged(UsageState state)
    {
        Dispatcher.BeginInvoke(() => _viewModel?.Apply(state));
    }

    private void OnActivationRequested()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            _window?.ShowNearTray();
            if (_monitor is not null)
                await RefreshSafelyAsync(() => _monitor.RefreshAsync(CancellationToken.None));
        });
    }

    private void OnResumed()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_monitor is not null)
                await RefreshSafelyAsync(() => _monitor.OnResumeAsync(CancellationToken.None));
        });
    }

    private async Task RefreshSafelyAsync(Func<Task> refresh)
    {
        try { await refresh(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { await LogAsync($"refresh-error={exception}"); }
    }

    private void OnExitRequested() => _ = ShutdownApplicationAsync(0);

    private void OnBackgroundError(Exception exception) =>
        _ = LogAsync($"background-error={exception}");

    private async Task ShutdownApplicationAsync(int exitCode)
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
            return;
        try
        {
            if (_lifecycle is not null)
                await _lifecycle.ShutdownAsync();
            else if (_singleInstance is not null)
                await _singleInstance.DisposeAsync();
        }
        catch (Exception exception)
        {
            await LogAsync($"shutdown-error={exception}");
            exitCode = 1;
        }
        finally
        {
            DispatcherUnhandledException -= OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            Shutdown(exitCode);
        }
    }

    private async void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        await LogAsync($"dispatcher-error={e.Exception}");
        await ShutdownApplicationAsync(1);
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _ = LogAsync($"process-error={exception}");
        TryEmergencyCleanup();
    }

    private void OnProcessExit(object? sender, EventArgs e) => TryEmergencyCleanup();

    private void TryEmergencyCleanup()
    {
        if (Interlocked.Exchange(ref _shutdownRequested, 1) != 0)
            return;
        try
        {
            _lifecycle?.ShutdownAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }
    }

    private Task LogAsync(string message) =>
        _log?.WriteAsync(message) ?? Task.CompletedTask;

    private static bool IsStartupWriteDisabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("CODEX_TOKEN_BAR_DISABLE_STARTUP_WRITE"),
            "1",
            StringComparison.Ordinal);

    private static string GetExecutablePath()
    {
        var assemblyPath = Assembly.GetExecutingAssembly().Location;
        var appHostPath = Path.ChangeExtension(assemblyPath, ".exe");
        if (File.Exists(appHostPath))
            return appHostPath;
        return Environment.ProcessPath ?? assemblyPath;
    }

    private async Task WriteSmokeEvidenceAsync(UsageState state)
    {
        var output = Environment.GetEnvironmentVariable("CODEX_TOKEN_BAR_SMOKE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
            return;

        var safe = new
        {
            freshness = state.Freshness.ToString(),
            limitId = state.PrimaryQuota?.LimitId,
            windowDurationMins = QuotaSelector.WeeklyWindowMinutes,
            remainingPercent = state.PrimaryQuota?.RemainingPercent,
            resetsAt = state.PrimaryQuota?.ResetsAt?.ToUnixTimeSeconds(),
            appServerProcessId = Volatile.Read(ref _ownedProcessId),
        };
        var absoluteOutput = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput)!);
        await File.WriteAllTextAsync(
            absoluteOutput,
            JsonSerializer.Serialize(safe, new JsonSerializerOptions { WriteIndented = true }));
        _ = Dispatcher.BeginInvoke(() => _ = ShutdownApplicationAsync(0));
    }

    private async Task RunDesignPreviewAsync(string[] args)
    {
        var dark = args.Contains("--dark", StringComparer.Ordinal);
        var viewModel = new SummaryViewModel();
        viewModel.Apply(new UsageState(
            UsageFreshness.Fresh,
            new QuotaView("codex", "codex", 47, DateTimeOffset.Now.AddDays(7)),
            [new QuotaView("extra", "额外额度", 82, DateTimeOffset.Now.AddDays(7))],
            DateTimeOffset.Now,
            null,
            null));
        var window = new SummaryWindow(viewModel, dark);
        var outputIndex = Array.IndexOf(args, "--output");
        if (outputIndex >= 0 && outputIndex + 1 < args.Length)
        {
            var output = Path.GetFullPath(args[outputIndex + 1]);
            var scaleIndex = Array.IndexOf(args, "--scale");
            var scale = scaleIndex >= 0 && scaleIndex + 1 < args.Length &&
                double.TryParse(args[scaleIndex + 1], System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 1d;
            window.Show();
            window.UpdateLayout();
            var width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth * scale));
            var height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight * scale));
            var bitmap = new RenderTargetBitmap(
                width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(window);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var stream = File.Create(output))
                encoder.Save(stream);
            window.CloseForExit();
            Shutdown(0);
            return;
        }

        window.IsVisibleChanged += (_, _) =>
        {
            if (!window.IsVisible)
                Shutdown(0);
        };
        window.ShowNearTray();
    }
}
