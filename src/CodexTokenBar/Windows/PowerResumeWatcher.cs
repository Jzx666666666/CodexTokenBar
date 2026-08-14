using Microsoft.Win32;

namespace CodexTokenBar.Windows;

public interface IPowerModeSource
{
    event PowerModeChangedEventHandler? PowerModeChanged;
}

public sealed class PowerResumeWatcher : IDisposable
{
    private readonly IPowerModeSource _source;
    private int _disposed;

    public PowerResumeWatcher(IPowerModeSource? source = null)
    {
        _source = source ?? new SystemPowerModeSource();
        _source.PowerModeChanged += OnPowerModeChanged;
    }

    public event Action? Resumed;

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
            Resumed?.Invoke();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _source.PowerModeChanged -= OnPowerModeChanged;
    }

    private sealed class SystemPowerModeSource : IPowerModeSource
    {
        public event PowerModeChangedEventHandler? PowerModeChanged
        {
            add => SystemEvents.PowerModeChanged += value;
            remove => SystemEvents.PowerModeChanged -= value;
        }
    }
}
