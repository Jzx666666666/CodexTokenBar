using System.IO;
using Microsoft.Win32;

namespace CodexTokenBar.Windows;

public interface IStartupRegistration
{
    Task ApplyAsync(bool enabled, string executablePath, CancellationToken cancellationToken);
}

public interface IUserRunRegistry
{
    void SetValue(string keyPath, string valueName, string value);
    void DeleteValue(string keyPath, string valueName);
}

public sealed class StartupRegistration : IStartupRegistration
{
    public const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "CodexTokenBar";
    private readonly IUserRunRegistry _registry;

    public StartupRegistration(IUserRunRegistry? registry = null)
    {
        _registry = registry ?? new CurrentUserRunRegistry();
    }

    public Task ApplyAsync(bool enabled, string executablePath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        var absolutePath = Path.GetFullPath(executablePath);
        if (enabled)
            _registry.SetValue(RunKeyPath, ValueName, $"\"{absolutePath}\" --startup");
        else
            _registry.DeleteValue(RunKeyPath, ValueName);
        return Task.CompletedTask;
    }

    private sealed class CurrentUserRunRegistry : IUserRunRegistry
    {
        public void SetValue(string keyPath, string valueName, string value)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开当前用户开机启动注册表项");
            key.SetValue(valueName, value, RegistryValueKind.String);
        }

        public void DeleteValue(string keyPath, string valueName)
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true)
                ?? throw new InvalidOperationException("无法打开当前用户开机启动注册表项");
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}
