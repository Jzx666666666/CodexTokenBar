using CodexTokenBar.Codex;
using CodexTokenBar.Domain;

namespace CodexTokenBar.Tests;

internal static class ProcessSupervisorTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("Process.LocatorUsesRequiredPrecedence", LocatorUsesRequiredPrecedence),
        new("Process.LocatorRejectsMissingCodex", LocatorRejectsMissingCodex),
        new("Process.StartFailureFallsBackToNextCodex", StartFailureFallsBackToNextCodex),
        new("Process.BuildsSafeCmdAndExeSpecifications", BuildsSafeCmdAndExeSpecifications),
        new("Process.FakeServerReadsWeeklyQuota", FakeServerReadsWeeklyQuota),
        new("Process.FakeMalformedOutputFails", FakeMalformedOutputFails),
        new("Process.FakeEarlyExitFails", FakeEarlyExitFails),
        new("Process.FakeDelayHonorsCancellation", FakeDelayHonorsCancellation),
        new("Process.DisposeStopsOwnedChild", DisposeStopsOwnedChild),
        new("Process.StderrIsDrainedToDiagnosticSink", StderrIsDrainedToDiagnosticSink),
        new("Process.ProbeOutputIsRedacted", ProbeOutputIsRedacted),
    ];

    private static Task LocatorUsesRequiredPrecedence()
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            @"C:\tools\codex",
            @"C:\tools\codex.cmd",
            @"C:\tools\codex.exe",
        };
        var locator = new CodexCommandLocator(
            () => @"C:\tools",
            files.Contains);

        Assert.Equal(@"C:\tools\codex.exe", locator.Find());
        Assert.SequenceEqual(
            new[] { @"C:\tools\codex.exe", @"C:\tools\codex.cmd", @"C:\tools\codex" },
            locator.FindAll());
        files.Remove(@"C:\tools\codex.exe");
        Assert.Equal(@"C:\tools\codex.cmd", locator.Find());
        files.Remove(@"C:\tools\codex.cmd");
        Assert.Equal(@"C:\tools\codex", locator.Find());
        return Task.CompletedTask;
    }

    private static async Task StartFailureFallsBackToNextCodex()
    {
        var attempted = new List<string>();
        var expected = new ScriptedAppServerConnection();
        var factory = new CodexConnectionFactory(
            ["blocked.exe", "working.cmd"],
            (command, _) =>
            {
                attempted.Add(command);
                return command == "blocked.exe"
                    ? Task.FromException<IAppServerConnection>(new CodexProcessException("access denied"))
                    : Task.FromResult<IAppServerConnection>(expected);
            });

        var actual = await factory.StartAsync(CancellationToken.None);

        Assert.Equal(expected, actual);
        Assert.SequenceEqual(new[] { "blocked.exe", "working.cmd" }, attempted);
    }

    private static Task LocatorRejectsMissingCodex()
    {
        var locator = new CodexCommandLocator(() => @"C:\empty", _ => false);
        var error = Assert.Throws<CodexProcessException>(() => locator.Find());
        Assert.Equal("未找到 Codex CLI，请先安装并登录 Codex", error.Message);
        return Task.CompletedTask;
    }

    private static Task BuildsSafeCmdAndExeSpecifications()
    {
        var cmd = CodexProcessSupervisor.CreateStartInfo(@"C:\path with spaces\codex.cmd");
        Assert.Equal(Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe", cmd.FileName);
        Assert.Equal("/d /s /c \"\"C:\\path with spaces\\codex.cmd\" app-server\"", cmd.Arguments);
        Assert.Equal(0, cmd.ArgumentList.Count);
        Assert.Equal(false, cmd.UseShellExecute);
        Assert.Equal(true, cmd.CreateNoWindow);

        var exe = CodexProcessSupervisor.CreateStartInfo(@"C:\tools\codex.exe");
        Assert.Equal(@"C:\tools\codex.exe", exe.FileName);
        Assert.SequenceEqual(new[] { "app-server" }, exe.ArgumentList);
        return Task.CompletedTask;
    }

    private static async Task FakeServerReadsWeeklyQuota()
    {
        await using var supervisor = FakeSupervisor();
        await using var reader = new CodexRateLimitReader(supervisor.StartAsync);

        var selected = QuotaSelector.Select(await reader.ReadAsync(CancellationToken.None));

        Assert.Equal(99, selected.Primary.RemainingPercent);
    }

    private static async Task FakeMalformedOutputFails()
    {
        await using var supervisor = FakeSupervisor("--malformed");
        await using var reader = new CodexRateLimitReader(supervisor.StartAsync);

        await Assert.ThrowsAsync<CodexProtocolException>(() => reader.ReadAsync(CancellationToken.None));
    }

    private static async Task FakeEarlyExitFails()
    {
        await using var supervisor = FakeSupervisor("--exit-after-initialize");
        await using var reader = new CodexRateLimitReader(supervisor.StartAsync);

        await Assert.ThrowsAsync<CodexProtocolException>(() => reader.ReadAsync(CancellationToken.None));
    }

    private static async Task FakeDelayHonorsCancellation()
    {
        await using var supervisor = FakeSupervisor("--delay-ms", "5000");
        await using var reader = new CodexRateLimitReader(supervisor.StartAsync);
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAsync<OperationCanceledException>(() => reader.ReadAsync(timeout.Token));
    }

    private static async Task DisposeStopsOwnedChild()
    {
        await using var supervisor = FakeSupervisor("--delay-ms", "5000");
        var connection = await supervisor.StartAsync(CancellationToken.None);
        var processId = connection.ProcessId;

        await connection.DisposeAsync();

        await WaitUntilAsync(() => !System.Diagnostics.Process.GetProcesses().Any(p => p.Id == processId), TimeSpan.FromSeconds(5));
    }

    private static async Task StderrIsDrainedToDiagnosticSink()
    {
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var fakeDll = FakeServerPath();
        await using var supervisor = new CodexProcessSupervisor(
            FindDotnetHost(),
            [fakeDll, "--stderr-line", "safe diagnostic"],
            (line, _) =>
            {
                received.TrySetResult(line);
                return Task.CompletedTask;
            });
        await using var reader = new CodexRateLimitReader(supervisor.StartAsync);

        await reader.ReadAsync(CancellationToken.None);

        Assert.Equal("safe diagnostic", await received.Task.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    private static Task ProbeOutputIsRedacted()
    {
        var view = new QuotaView("codex", "codex", 99, DateTimeOffset.FromUnixTimeSeconds(1_787_132_210));
        var output = ProbeRunner.Format(view);

        Assert.Equal("limitId=codex\nwindowDurationMins=10080\nremainingPercent=99\nresetsAt=1787132210", output.Replace("\r\n", "\n"));
        Assert.Equal(false, output.Contains("credit", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(false, output.Contains("token", StringComparison.OrdinalIgnoreCase));
        return Task.CompletedTask;
    }

    private static CodexProcessSupervisor FakeSupervisor(params string[] fixtureArgs)
    {
        return new CodexProcessSupervisor(
            FindDotnetHost(),
            [FakeServerPath(), .. fixtureArgs]);
    }

    private static string FakeServerPath() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..", "CodexTokenBar.FakeAppServer", "bin", "Debug", "net8.0", "CodexTokenBar.FakeAppServer.dll"));

    private static string FindDotnetHost()
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var host = paths
            .Select(path => Path.Combine(path.Trim('"'), "dotnet.exe"))
            .FirstOrDefault(File.Exists);
        return host ?? throw new InvalidOperationException("dotnet.exe was not found on PATH.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("Condition was not met before timeout.");
            await Task.Delay(25);
        }
    }
}
