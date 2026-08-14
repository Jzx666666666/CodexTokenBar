using CodexTokenBar.Domain;

namespace CodexTokenBar.Persistence;

public sealed record AppSettings(bool StartWithWindows)
{
    public static AppSettings Default { get; } = new(true);
}

public sealed record StoredQuotaSnapshot(
    QuotaView PrimaryQuota,
    IReadOnlyList<QuotaView> OtherQuotas,
    DateTimeOffset LastSuccessfulUpdate);

public interface IAppStateStore
{
    Task<AppSettings> LoadSettingsAsync(CancellationToken cancellationToken);
    Task SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken);
    Task<StoredQuotaSnapshot?> LoadLastSnapshotAsync(CancellationToken cancellationToken);
    Task SaveLastSnapshotAsync(StoredQuotaSnapshot snapshot, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}
