using CodexTokenBar.Domain;
using System.IO;

namespace CodexTokenBar.Codex;

public static class ProbeRunner
{
    public static async Task<int> RunAsync(
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        try
        {
            var commands = new CodexCommandLocator().FindAll();
            var factory = new CodexConnectionFactory(commands);
            await using var reader = new CodexRateLimitReader(factory.StartAsync);
            var snapshot = await reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var selected = QuotaSelector.Select(snapshot);
            await output.WriteLineAsync(Format(selected.Primary)).ConfigureAwait(false);
            return 0;
        }
        catch (Exception exception)
        {
            _ = exception;
            await error.WriteLineAsync("probe-error=无法读取 Codex 周额度").ConfigureAwait(false);
            return 1;
        }
    }

    public static string Format(QuotaView quota)
    {
        ArgumentNullException.ThrowIfNull(quota);
        var resetsAt = quota.ResetsAt?.ToUnixTimeSeconds().ToString() ?? "null";
        return string.Join(Environment.NewLine,
            $"limitId={quota.LimitId}",
            $"windowDurationMins={QuotaSelector.WeeklyWindowMinutes}",
            $"remainingPercent={quota.RemainingPercent}",
            $"resetsAt={resetsAt}");
    }
}
