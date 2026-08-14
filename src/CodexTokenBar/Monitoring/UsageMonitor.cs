using CodexTokenBar.Domain;
using CodexTokenBar.Persistence;
using CodexTokenBar.Codex;

namespace CodexTokenBar.Monitoring;

public sealed class UsageMonitor : IAsyncDisposable
{
    private static readonly TimeSpan NormalRefreshDelay = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultReadTimeout = TimeSpan.FromSeconds(20);

    private readonly IRateLimitReader _reader;
    private readonly IAppStateStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly TimeSpan _readTimeout;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _refreshGate = new();
    private Task? _inFlight;
    private Task? _loop;
    private CancellationTokenSource? _timerWake;
    private int _failureCount;
    private int _started;
    private int _disposed;

    public UsageMonitor(
        IRateLimitReader reader,
        IAppStateStore store,
        Func<DateTimeOffset>? clock = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        TimeSpan? readTimeout = null)
    {
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _clock = clock ?? (() => DateTimeOffset.Now);
        _delay = delay ?? Task.Delay;
        _readTimeout = readTimeout ?? DefaultReadTimeout;
    }

    public UsageState State { get; private set; } = UsageState.Starting;
    public event Action<UsageState>? StateChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0)
            return;

        var cached = await _store.LoadLastSnapshotAsync(cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            Publish(new UsageState(
                UsageFreshness.Stale,
                cached.PrimaryQuota,
                cached.OtherQuotas,
                cached.LastSuccessfulUpdate,
                "正在获取最新额度",
                null));
        }

        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        _loop = RunLoopAsync(_lifetime.Token);
    }

    public Task RefreshAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        Task task;
        TaskCompletionSource? completion = null;
        lock (_refreshGate)
        {
            if (_inFlight is not null)
                return _inFlight;

            completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            task = completion.Task;
            _inFlight = task;
        }

        Interlocked.Exchange(ref _timerWake, null)?.Cancel();
        BeginRefresh(completion, cancellationToken);
        return task;
    }

    private async void BeginRefresh(TaskCompletionSource completion, CancellationToken cancellationToken)
    {
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            completion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
        finally
        {
            lock (_refreshGate)
                _inFlight = null;
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        if (State.Freshness == UsageFreshness.Fresh)
        {
            Publish(State with
            {
                Freshness = UsageFreshness.Refreshing,
                ErrorMessage = null,
                NextRetryAt = null,
            });
        }

        using var timeout = new CancellationTokenSource(_readTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _lifetime.Token, timeout.Token);
        try
        {
            var snapshot = await _reader.ReadAsync(linked.Token).ConfigureAwait(false);
            var selected = QuotaSelector.Select(snapshot);
            var now = _clock();
            var stored = new StoredQuotaSnapshot(selected.Primary, selected.Others, now);
            await _store.SaveLastSnapshotAsync(stored, linked.Token).ConfigureAwait(false);
            _failureCount = 0;
            Publish(new UsageState(
                UsageFreshness.Fresh,
                selected.Primary,
                selected.Others,
                now,
                null,
                null));
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || _lifetime.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            var delay = RetrySchedule.GetDelay(_failureCount++);
            var staleQuota = State.PrimaryQuota;
            var staleOthers = State.OtherQuotas;
            var lastUpdate = State.LastSuccessfulUpdate;
            Publish(new UsageState(
                UsageFreshness.Stale,
                staleQuota,
                staleOthers,
                lastUpdate,
                FormatError(exception),
                _clock().Add(delay)));
            try
            {
                await _reader.ResetConnectionAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }

    private static string FormatError(Exception exception) => exception switch
    {
        CodexProcessException process when process.Message.StartsWith("未找到 Codex CLI", StringComparison.Ordinal) =>
            process.Message,
        CodexProcessException => "无法启动 Codex CLI，请检查安装与登录状态",
        QuotaSelectionException selection => selection.Message,
        OperationCanceledException => "读取 Codex 周额度超时",
        CodexProtocolException => "Codex 未登录或额度协议不兼容",
        _ => "Codex 连接或响应异常",
    };

    private async Task RunLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var delay = State.Freshness == UsageFreshness.Stale
                ? RetrySchedule.GetDelay(Math.Max(0, _failureCount - 1))
                : NormalRefreshDelay;
            var wake = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var previous = Interlocked.Exchange(ref _timerWake, wake);
            previous?.Cancel();
            previous?.Dispose();
            try
            {
                await _delay(delay, wake.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (wake.IsCancellationRequested)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                Task? currentRefresh;
                lock (_refreshGate)
                    currentRefresh = _inFlight;
                if (currentRefresh is not null)
                    await currentRefresh.ConfigureAwait(false);
                continue;
            }
            finally
            {
                Interlocked.CompareExchange(ref _timerWake, null, wake);
                wake.Dispose();
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    public Task OnResumeAsync(CancellationToken cancellationToken)
    {
        return RefreshAsync(cancellationToken);
    }

    public async Task StopAsync()
    {
        _lifetime.Cancel();
        var timer = Interlocked.Exchange(ref _timerWake, null);
        timer?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Task? inFlight;
        lock (_refreshGate)
            inFlight = _inFlight;
        if (inFlight is not null)
        {
            try
            {
                await inFlight.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private void Publish(UsageState state)
    {
        State = state;
        StateChanged?.Invoke(state);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync().ConfigureAwait(false);
        await _reader.DisposeAsync().ConfigureAwait(false);
        _timerWake?.Dispose();
        _lifetime.Dispose();
    }
}
