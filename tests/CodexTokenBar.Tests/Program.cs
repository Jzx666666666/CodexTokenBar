using CodexTokenBar.Tests;

return await TestRunner.RunAsync(
    args,
    DomainTests.Cases,
    JsonRpcTests.Cases,
    ProcessSupervisorTests.Cases,
    UsageMonitorTests.Cases,
    PersistenceTests.Cases,
    WindowsAdapterTests.Cases,
    WindowsTaskbarProbeTests.Cases,
    TrayRenderingTests.Cases,
    TaskbarAnchorTests.Cases,
    TaskbarRingRenderingTests.Cases,
    TaskbarOverlayTests.Cases,
    TaskbarZOrderWatcherTests.Cases,
    ViewModelTests.Cases,
    ApplicationLifecycleTests.Cases);
