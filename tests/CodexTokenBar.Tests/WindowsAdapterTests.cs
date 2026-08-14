using CodexTokenBar.Windows;
using Microsoft.Win32;

namespace CodexTokenBar.Tests;

internal static class WindowsAdapterTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("Windows.StartupUsesExactRunKeyAndQuotedCommand", StartupUsesExactRunKeyAndQuotedCommand),
        new("Windows.StartupDisableIsIdempotent", StartupDisableIsIdempotent),
        new("Windows.StartupRepairsMovedPathOnlyWhenEnabled", StartupRepairsMovedPathOnlyWhenEnabled),
        new("Windows.SingleInstanceForwardsOneShowCommand", SingleInstanceForwardsOneShowCommand),
        new("Windows.SingleInstanceRejectsUnknownCommand", SingleInstanceRejectsUnknownCommand),
        new("Windows.PowerWatcherRaisesResumeOnly", PowerWatcherRaisesResumeOnly),
        new("Windows.PowerWatcherUnsubscribesOnDispose", PowerWatcherUnsubscribesOnDispose),
    ];

    private static async Task StartupUsesExactRunKeyAndQuotedCommand()
    {
        var registry = new MemoryRunRegistry();
        IStartupRegistration startup = new StartupRegistration(registry);

        await startup.ApplyAsync(true, @"D:\Portable Apps\CodexTokenBar.exe", CancellationToken.None);

        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", registry.LastKeyPath);
        Assert.Equal("CodexTokenBar", registry.LastValueName);
        Assert.Equal("\"D:\\Portable Apps\\CodexTokenBar.exe\" --startup", registry.Value);
    }

    private static async Task StartupDisableIsIdempotent()
    {
        var registry = new MemoryRunRegistry { Value = "old" };
        var startup = new StartupRegistration(registry);
        await startup.ApplyAsync(false, @"D:\CodexTokenBar.exe", CancellationToken.None);
        await startup.ApplyAsync(false, @"D:\CodexTokenBar.exe", CancellationToken.None);

        Assert.Equal<string?>(null, registry.Value);
        Assert.Equal(2, registry.DeleteCount);
    }

    private static async Task StartupRepairsMovedPathOnlyWhenEnabled()
    {
        var registry = new MemoryRunRegistry { Value = "\"C:\\Old\\CodexTokenBar.exe\" --startup" };
        var startup = new StartupRegistration(registry);
        await startup.ApplyAsync(true, @"D:\New\CodexTokenBar.exe", CancellationToken.None);
        Assert.Equal("\"D:\\New\\CodexTokenBar.exe\" --startup", registry.Value);

        registry.Value = "keep-disabled-preference-out-of-registry";
        await startup.ApplyAsync(false, @"E:\Moved\CodexTokenBar.exe", CancellationToken.None);
        Assert.Equal<string?>(null, registry.Value);
    }

    private static async Task SingleInstanceForwardsOneShowCommand()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var first = new SingleInstanceCoordinator(suffix, "test-user");
        await using var second = new SingleInstanceCoordinator(suffix, "test-user");
        Assert.Equal(true, await first.TryAcquireAsync(CancellationToken.None));
        Assert.Equal(false, await second.TryAcquireAsync(CancellationToken.None));
        var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var count = 0;
        first.ActivationRequested += () =>
        {
            Interlocked.Increment(ref count);
            activated.TrySetResult();
        };

        await second.SendShowAsync(CancellationToken.None);
        await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, count);
    }

    private static async Task SingleInstanceRejectsUnknownCommand()
    {
        var suffix = Guid.NewGuid().ToString("N");
        await using var first = new SingleInstanceCoordinator(suffix, "test-user");
        await using var second = new SingleInstanceCoordinator(suffix, "test-user");
        Assert.Equal(true, await first.TryAcquireAsync(CancellationToken.None));
        var count = 0;
        first.ActivationRequested += () => Interlocked.Increment(ref count);

        await second.SendAsync("delete", CancellationToken.None);
        await Task.Delay(100);
        Assert.Equal(0, count);
    }

    private static Task PowerWatcherRaisesResumeOnly()
    {
        var source = new FakePowerModeSource();
        using var watcher = new PowerResumeWatcher(source);
        var count = 0;
        watcher.Resumed += () => count++;

        source.Raise(PowerModes.Suspend);
        source.Raise(PowerModes.StatusChange);
        source.Raise(PowerModes.Resume);
        Assert.Equal(1, count);
        return Task.CompletedTask;
    }

    private static Task PowerWatcherUnsubscribesOnDispose()
    {
        var source = new FakePowerModeSource();
        var watcher = new PowerResumeWatcher(source);
        Assert.Equal(1, source.SubscriberCount);
        watcher.Dispose();
        Assert.Equal(0, source.SubscriberCount);
        return Task.CompletedTask;
    }
}

internal sealed class MemoryRunRegistry : IUserRunRegistry
{
    public string? Value { get; set; }
    public string? LastKeyPath { get; private set; }
    public string? LastValueName { get; private set; }
    public int DeleteCount { get; private set; }

    public void SetValue(string keyPath, string valueName, string value)
    {
        LastKeyPath = keyPath;
        LastValueName = valueName;
        Value = value;
    }

    public void DeleteValue(string keyPath, string valueName)
    {
        LastKeyPath = keyPath;
        LastValueName = valueName;
        DeleteCount++;
        Value = null;
    }
}

internal sealed class FakePowerModeSource : IPowerModeSource
{
    private PowerModeChangedEventHandler? _powerModeChanged;
    public int SubscriberCount { get; private set; }
    public event PowerModeChangedEventHandler? PowerModeChanged
    {
        add { _powerModeChanged += value; SubscriberCount++; }
        remove { _powerModeChanged -= value; SubscriberCount--; }
    }

    public void Raise(PowerModes mode) =>
        _powerModeChanged?.Invoke(this, new PowerModeChangedEventArgs(mode));
}
