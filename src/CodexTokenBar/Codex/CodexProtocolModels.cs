using System.Text.Json.Serialization;

namespace CodexTokenBar.Codex;

internal sealed record GetRateLimitsResponse(
    [property: JsonPropertyName("rateLimits")] RateLimitPoolDto? RateLimits,
    [property: JsonPropertyName("rateLimitsByLimitId")] Dictionary<string, RateLimitPoolDto>? RateLimitsByLimitId);

internal sealed record RateLimitPoolDto(
    [property: JsonPropertyName("limitId")] string? LimitId,
    [property: JsonPropertyName("limitName")] string? LimitName,
    [property: JsonPropertyName("primary")] RateLimitWindowDto? Primary,
    [property: JsonPropertyName("secondary")] RateLimitWindowDto? Secondary);

internal sealed record RateLimitWindowDto(
    [property: JsonPropertyName("usedPercent")] int? UsedPercent,
    [property: JsonPropertyName("windowDurationMins")] int? WindowDurationMins,
    [property: JsonPropertyName("resetsAt")] long? ResetsAt);
