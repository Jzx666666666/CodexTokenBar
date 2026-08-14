using System.IO;

namespace CodexTokenBar.Persistence;

public sealed class AppPaths
{
    public AppPaths(string? rootDirectory = null)
    {
        RootDirectory = Path.GetFullPath(rootDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexTokenBar"));
    }

    public string RootDirectory { get; }
    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");
    public string SnapshotFile => Path.Combine(RootDirectory, "snapshot.json");
    public string LogFile => Path.Combine(RootDirectory, "app.log");
    public string RotatedLogFile(int index) => Path.Combine(RootDirectory, $"app.{index}.log");
}
