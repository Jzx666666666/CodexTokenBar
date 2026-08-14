using CodexTokenBar.Domain;
using CodexTokenBar.Persistence;

namespace CodexTokenBar.Tests;

internal static class PersistenceTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("Persistence.MissingFilesYieldDefaults", MissingFilesYieldDefaults),
        new("Persistence.DisabledStartupPreferenceRoundTrips", DisabledStartupPreferenceRoundTrips),
        new("Persistence.SnapshotReplacementIsValidJson", SnapshotReplacementIsValidJson),
        new("Persistence.DamagedSnapshotIsIgnored", DamagedSnapshotIsIgnored),
        new("Persistence.ClearDeletesOnlyOwnedFiles", ClearDeletesOnlyOwnedFiles),
        new("Persistence.LogRedactsSensitiveKeys", LogRedactsSensitiveKeys),
        new("Persistence.LogRotatesAndRetainsSevenFiles", LogRotatesAndRetainsSevenFiles),
    ];

    private static async Task MissingFilesYieldDefaults()
    {
        using var fixture = new StoreFixture();
        Assert.Equal(true, (await fixture.Store.LoadSettingsAsync(CancellationToken.None)).StartWithWindows);
        Assert.Equal<StoredQuotaSnapshot?>(null, await fixture.Store.LoadLastSnapshotAsync(CancellationToken.None));
    }

    private static async Task DisabledStartupPreferenceRoundTrips()
    {
        using var fixture = new StoreFixture();
        await fixture.Store.SaveSettingsAsync(new AppSettings(false), CancellationToken.None);
        Assert.Equal(false, (await fixture.Store.LoadSettingsAsync(CancellationToken.None)).StartWithWindows);
    }

    private static async Task SnapshotReplacementIsValidJson()
    {
        using var fixture = new StoreFixture();
        await fixture.Store.SaveLastSnapshotAsync(CreateSnapshot(47), CancellationToken.None);
        await fixture.Store.SaveLastSnapshotAsync(CreateSnapshot(46), CancellationToken.None);

        var loaded = await fixture.Store.LoadLastSnapshotAsync(CancellationToken.None);
        Assert.Equal(46, loaded?.PrimaryQuota.RemainingPercent);
        Assert.Equal(false, File.Exists(fixture.Paths.SnapshotFile + ".tmp"));
    }

    private static async Task DamagedSnapshotIsIgnored()
    {
        using var fixture = new StoreFixture();
        await File.WriteAllTextAsync(fixture.Paths.SnapshotFile, "{broken");
        Assert.Equal<StoredQuotaSnapshot?>(null, await fixture.Store.LoadLastSnapshotAsync(CancellationToken.None));
    }

    private static async Task ClearDeletesOnlyOwnedFiles()
    {
        using var fixture = new StoreFixture();
        var unrelated = Path.Combine(fixture.Paths.RootDirectory, "keep.me");
        await File.WriteAllTextAsync(unrelated, "keep");
        await fixture.Store.SaveSettingsAsync(new AppSettings(false), CancellationToken.None);
        await fixture.Store.SaveLastSnapshotAsync(CreateSnapshot(47), CancellationToken.None);
        await File.WriteAllTextAsync(fixture.Paths.LogFile, "log");

        await fixture.Store.ClearAsync(CancellationToken.None);

        Assert.Equal(true, File.Exists(unrelated));
        Assert.Equal(false, File.Exists(fixture.Paths.SettingsFile));
        Assert.Equal(false, File.Exists(fixture.Paths.SnapshotFile));
        Assert.Equal(false, File.Exists(fixture.Paths.LogFile));
    }

    private static async Task LogRedactsSensitiveKeys()
    {
        using var fixture = new StoreFixture();
        var log = new RollingLog(fixture.Paths, maxBytes: 1_024);
        await log.WriteAsync("token=abc authorization: Bearer xyz safe=value authCookie=secret");
        var text = await File.ReadAllTextAsync(fixture.Paths.LogFile);

        Assert.Equal(false, text.Contains("abc", StringComparison.Ordinal));
        Assert.Equal(false, text.Contains("Bearer", StringComparison.Ordinal));
        Assert.Equal(false, text.Contains("xyz", StringComparison.Ordinal));
        Assert.Equal(false, text.Contains("secret", StringComparison.Ordinal));
        Assert.Equal(true, text.Contains("safe=value", StringComparison.Ordinal));
    }

    private static async Task LogRotatesAndRetainsSevenFiles()
    {
        using var fixture = new StoreFixture();
        var log = new RollingLog(fixture.Paths, maxBytes: 80, retainedFiles: 7);
        for (var index = 0; index < 20; index++)
            await log.WriteAsync($"message-{index:D2}-xxxxxxxxxxxxxxxxxxxxxxxx");

        var logs = Directory.GetFiles(fixture.Paths.RootDirectory, "app*.log");
        Assert.Equal(true, logs.Length <= 7);
        Assert.Equal(true, File.Exists(fixture.Paths.LogFile));
    }

    private static StoredQuotaSnapshot CreateSnapshot(int remaining) => new(
        new QuotaView("codex", "codex", remaining, DateTimeOffset.FromUnixTimeSeconds(1_787_132_210)),
        Array.Empty<QuotaView>(),
        new DateTimeOffset(2026, 8, 12, 16, 42, 0, TimeSpan.FromHours(8)));

    private sealed class StoreFixture : IDisposable
    {
        public AppPaths Paths { get; }
        public AppStateStore Store { get; }

        public StoreFixture()
        {
            var root = Path.Combine(Path.GetTempPath(), "CodexTokenBar.Tests", Guid.NewGuid().ToString("N"));
            Paths = new AppPaths(root);
            Directory.CreateDirectory(Paths.RootDirectory);
            Store = new AppStateStore(Paths);
        }

        public void Dispose()
        {
            if (Directory.Exists(Paths.RootDirectory))
                Directory.Delete(Paths.RootDirectory, recursive: true);
        }
    }
}
