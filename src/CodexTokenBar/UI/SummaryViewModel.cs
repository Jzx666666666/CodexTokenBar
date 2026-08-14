using CodexTokenBar.Domain;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace CodexTokenBar.UI;

public sealed class SummaryViewModel : INotifyPropertyChanged
{
    private readonly TimeZoneInfo _localTimeZone;
    private string _remainingText = "—";
    private string _statusText = "正在读取额度";
    private string _resetText = "下次重置：—";
    private string _lastUpdatedText = "尚无成功数据";
    private string _errorText = string.Empty;
    private string _nextRetryText = string.Empty;
    private bool _hasStaleQuota;
    private bool _hasOtherQuotas;
    private bool _hasError;
    private QuotaTone _tone = QuotaTone.Gray;

    public SummaryViewModel(TimeZoneInfo? localTimeZone = null)
    {
        _localTimeZone = localTimeZone ?? TimeZoneInfo.Local;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string RemainingText { get => _remainingText; private set => Set(ref _remainingText, value); }
    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public string ResetText { get => _resetText; private set => Set(ref _resetText, value); }
    public string LastUpdatedText { get => _lastUpdatedText; private set => Set(ref _lastUpdatedText, value); }
    public string ErrorText { get => _errorText; private set => Set(ref _errorText, value); }
    public string NextRetryText { get => _nextRetryText; private set => Set(ref _nextRetryText, value); }
    public string RefreshCadenceText => "每 60 秒自动刷新";
    public bool HasStaleQuota { get => _hasStaleQuota; private set => Set(ref _hasStaleQuota, value); }
    public bool HasOtherQuotas { get => _hasOtherQuotas; private set => Set(ref _hasOtherQuotas, value); }
    public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
    public QuotaTone Tone { get => _tone; private set => Set(ref _tone, value); }
    public ObservableCollection<OtherQuotaViewModel> OtherQuotas { get; } = [];

    public void Apply(UsageState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var quota = state.PrimaryQuota;
        HasStaleQuota = state.Freshness == UsageFreshness.Stale && quota is not null;
        var isTrusted = state.Freshness is UsageFreshness.Fresh or UsageFreshness.Refreshing;

        RemainingText = quota is null ? "—" : $"{quota.RemainingPercent}%";
        Tone = isTrusted && quota is not null
            ? QuotaPresentation.GetTone(quota.RemainingPercent)
            : QuotaTone.Gray;
        StatusText = GetStatusText(state, quota);
        ResetText = $"下次重置：{FormatDateTime(quota?.ResetsAt)}";
        LastUpdatedText = FormatLastUpdated(state);
        ErrorText = state.ErrorMessage ?? string.Empty;
        HasError = !string.IsNullOrWhiteSpace(state.ErrorMessage);
        NextRetryText = state.NextRetryAt is { } retry
            ? $"下次重试：{FormatTime(retry)}"
            : string.Empty;

        OtherQuotas.Clear();
        foreach (var other in state.OtherQuotas)
        {
            OtherQuotas.Add(new OtherQuotaViewModel(
                other.DisplayName,
                $"{other.RemainingPercent}%",
                $"重置：{FormatDateTime(other.ResetsAt)}"));
        }
        HasOtherQuotas = OtherQuotas.Count > 0;
    }

    private static string GetStatusText(UsageState state, QuotaView? quota)
    {
        if (state.Freshness == UsageFreshness.Starting)
            return "正在读取额度";
        if (state.Freshness == UsageFreshness.Refreshing)
            return "正在刷新";
        if (state.Freshness == UsageFreshness.Stale)
            return quota is null ? "数据不可用" : "数据已过期";

        return QuotaPresentation.GetTone(quota?.RemainingPercent) switch
        {
            QuotaTone.Green => "状态良好",
            QuotaTone.Yellow => "额度偏低",
            QuotaTone.Red => "额度不足",
            _ => "数据不可用",
        };
    }

    private string FormatLastUpdated(UsageState state)
    {
        if (state.LastSuccessfulUpdate is not { } updated)
            return "尚无成功数据";
        var prefix = state.Freshness == UsageFreshness.Stale
            ? "最后成功更新："
            : "最后更新：";
        return prefix + FormatTime(updated);
    }

    private string FormatDateTime(DateTimeOffset? value)
    {
        if (value is null)
            return "—";
        var local = TimeZoneInfo.ConvertTime(value.Value, _localTimeZone);
        return $"{local:M月d日 HH:mm}";
    }

    private string FormatTime(DateTimeOffset value) =>
        TimeZoneInfo.ConvertTime(value, _localTimeZone).ToString("HH:mm");

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed record OtherQuotaViewModel(
    string DisplayName,
    string RemainingText,
    string ResetText);
