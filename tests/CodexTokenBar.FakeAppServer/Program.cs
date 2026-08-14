using System.Text.Json;

var malformed = args.Contains("--malformed", StringComparer.Ordinal);
var exitAfterInitialize = args.Contains("--exit-after-initialize", StringComparer.Ordinal);
var delayIndex = Array.IndexOf(args, "--delay-ms");
var delayMs = delayIndex >= 0 && delayIndex + 1 < args.Length
    ? int.Parse(args[delayIndex + 1])
    : 0;
var stderrIndex = Array.IndexOf(args, "--stderr-line");
if (stderrIndex >= 0 && stderrIndex + 1 < args.Length)
    await Console.Error.WriteLineAsync(args[stderrIndex + 1]);

while (await Console.In.ReadLineAsync() is { } line)
{
    using var request = JsonDocument.Parse(line.TrimStart('\uFEFF'));
    var root = request.RootElement;
    if (!root.TryGetProperty("method", out var methodElement))
        continue;
    var method = methodElement.GetString();
    if (!root.TryGetProperty("id", out var idElement))
        continue;
    var id = idElement.GetInt32();

    if (method == "initialize")
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            id,
            result = new { userAgent = "fake", platformFamily = "windows", platformOs = "windows" },
        }));
        if (exitAfterInitialize)
            return 0;
        continue;
    }

    if (method == "account/rateLimits/read")
    {
        if (delayMs > 0)
            await Task.Delay(delayMs);
        if (malformed)
        {
            Console.WriteLine("not-json");
            continue;
        }

        var pool = new
        {
            limitId = "codex",
            limitName = (string?)null,
            primary = new { usedPercent = 1, windowDurationMins = 10_080, resetsAt = 1_787_132_210L },
            secondary = (object?)null,
        };
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            id,
            result = new
            {
                rateLimits = pool,
                rateLimitsByLimitId = new Dictionary<string, object> { ["codex"] = pool },
            },
        }));
    }
}

return 0;
