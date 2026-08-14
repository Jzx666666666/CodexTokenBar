using System.Text.Json;
using CodexTokenBar.Codex;
using CodexTokenBar.Domain;

namespace CodexTokenBar.Tests;

internal static class JsonRpcTests
{
    public static IReadOnlyList<TestCase> Cases { get; } =
    [
        new("JsonRpc.HandshakePrecedesInitializedNotification", HandshakePrecedesInitializedNotification),
        new("JsonRpc.IgnoresNotificationsAndMatchesOutOfOrderIds", IgnoresNotificationsAndMatchesOutOfOrderIds),
        new("JsonRpc.MalformedJsonFailsPendingRequest", MalformedJsonFailsPendingRequest),
        new("JsonRpc.EndOfStreamFailsPendingRequest", EndOfStreamFailsPendingRequest),
        new("JsonRpc.CanceledRequestDoesNotPoisonNextRequest", CanceledRequestDoesNotPoisonNextRequest),
        new("JsonRpc.MapsVerifiedRateLimitPayload", MapsVerifiedRateLimitPayload),
        new("JsonRpc.FallsBackToLegacyRateLimits", FallsBackToLegacyRateLimits),
        new("JsonRpc.RejectsInvalidRateLimitPayload", RejectsInvalidRateLimitPayload),
        new("JsonRpc.ResetDisposesAndReconnects", ResetDisposesAndReconnects),
    ];

    private static async Task HandshakePrecedesInitializedNotification()
    {
        await using var connection = new ScriptedAppServerConnection();
        await using var client = new CodexAppServerClient(connection);
        var initialize = client.InitializeAsync(CancellationToken.None);

        var request = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        Assert.Equal("initialize", request.GetProperty("method").GetString());
        Assert.Equal(1, request.GetProperty("id").GetInt32());
        await connection.FeedLineAsync("{\"id\":1,\"result\":{\"userAgent\":\"fake\"}}");
        await initialize;

        var notification = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        Assert.Equal("initialized", notification.GetProperty("method").GetString());
        Assert.Equal(false, notification.TryGetProperty("id", out _));
    }

    private static async Task IgnoresNotificationsAndMatchesOutOfOrderIds()
    {
        await using var connection = new ScriptedAppServerConnection();
        await using var client = new CodexAppServerClient(connection, initialized: true, nextRequestId: 2);
        var first = client.SendAsync<JsonElement>("first", new { }, CancellationToken.None);
        var second = client.SendAsync<JsonElement>("second", new { }, CancellationToken.None);
        await connection.ReadWrittenLineAsync();
        await connection.ReadWrittenLineAsync();

        await connection.FeedLineAsync("{\"method\":\"account/rateLimits/updated\",\"params\":{}}");
        await connection.FeedLineAsync("{\"id\":3,\"result\":{\"value\":\"second\"}}");
        await connection.FeedLineAsync("{\"id\":2,\"result\":{\"value\":\"first\"}}");

        Assert.Equal("first", (await first).GetProperty("value").GetString());
        Assert.Equal("second", (await second).GetProperty("value").GetString());
    }

    private static async Task MalformedJsonFailsPendingRequest()
    {
        await using var connection = new ScriptedAppServerConnection();
        await using var client = new CodexAppServerClient(connection, initialized: true, nextRequestId: 2);
        var pending = client.SendAsync<JsonElement>("read", new { }, CancellationToken.None);
        await connection.ReadWrittenLineAsync();

        await connection.FeedLineAsync("not-json");

        await Assert.ThrowsAsync<CodexProtocolException>(() => pending);
    }

    private static async Task EndOfStreamFailsPendingRequest()
    {
        await using var connection = new ScriptedAppServerConnection();
        await using var client = new CodexAppServerClient(connection, initialized: true, nextRequestId: 2);
        var pending = client.SendAsync<JsonElement>("read", new { }, CancellationToken.None);
        await connection.ReadWrittenLineAsync();

        connection.CompleteOutput();

        await Assert.ThrowsAsync<CodexProtocolException>(() => pending);
    }

    private static async Task CanceledRequestDoesNotPoisonNextRequest()
    {
        await using var connection = new ScriptedAppServerConnection();
        await using var client = new CodexAppServerClient(connection, initialized: true, nextRequestId: 2);
        using var canceled = new CancellationTokenSource();
        var first = client.SendAsync<JsonElement>("first", new { }, canceled.Token);
        await connection.ReadWrittenLineAsync();
        canceled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => first);

        await connection.FeedLineAsync("{\"id\":2,\"result\":{\"late\":true}}");
        var second = client.SendAsync<JsonElement>("second", new { }, CancellationToken.None);
        var secondLine = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        var secondId = secondLine.GetProperty("id").GetInt32();
        await connection.FeedLineAsync($"{{\"id\":{secondId},\"result\":{{\"value\":7}}}}");

        Assert.Equal(7, (await second).GetProperty("value").GetInt32());
    }

    private static async Task MapsVerifiedRateLimitPayload()
    {
        var connection = new ScriptedAppServerConnection();
        var factory = new ConnectionFactory();
        factory.Enqueue(connection);
        await using var reader = new CodexRateLimitReader(factory.CreateAsync);
        var read = reader.ReadAsync(CancellationToken.None);
        await CompleteInitializeAsync(connection);
        var request = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        Assert.Equal("account/rateLimits/read", request.GetProperty("method").GetString());
        await connection.FeedLineAsync(VerifiedPayload(request.GetProperty("id").GetInt32()));

        var selected = QuotaSelector.Select(await read);

        Assert.Equal(99, selected.Primary.RemainingPercent);
        Assert.Equal("codex", selected.Primary.LimitId);
    }

    private static async Task FallsBackToLegacyRateLimits()
    {
        var connection = new ScriptedAppServerConnection();
        var factory = new ConnectionFactory();
        factory.Enqueue(connection);
        await using var reader = new CodexRateLimitReader(factory.CreateAsync);
        var read = reader.ReadAsync(CancellationToken.None);
        await CompleteInitializeAsync(connection);
        var request = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        await connection.FeedLineAsync(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt32(),
            result = new
            {
                rateLimits = PoolPayload(25),
                rateLimitsByLimitId = (object?)null,
            },
        }));

        var snapshot = await read;

        Assert.Equal(1, snapshot.Pools.Count);
        Assert.Equal(75, QuotaSelector.Select(snapshot).Primary.RemainingPercent);
    }

    private static async Task RejectsInvalidRateLimitPayload()
    {
        var connection = new ScriptedAppServerConnection();
        var factory = new ConnectionFactory();
        factory.Enqueue(connection);
        await using var reader = new CodexRateLimitReader(factory.CreateAsync);
        var read = reader.ReadAsync(CancellationToken.None);
        await CompleteInitializeAsync(connection);
        var request = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        await connection.FeedLineAsync(JsonSerializer.Serialize(new
        {
            id = request.GetProperty("id").GetInt32(),
            result = new
            {
                rateLimits = new { limitId = (string?)null, primary = (object?)null, secondary = (object?)null },
            },
        }));

        await Assert.ThrowsAsync<CodexProtocolException>(() => read);
    }

    private static async Task ResetDisposesAndReconnects()
    {
        var first = new ScriptedAppServerConnection();
        var second = new ScriptedAppServerConnection();
        var factory = new ConnectionFactory();
        factory.Enqueue(first);
        factory.Enqueue(second);
        await using var reader = new CodexRateLimitReader(factory.CreateAsync);

        var firstRead = reader.ReadAsync(CancellationToken.None);
        await CompleteInitializeAsync(first);
        var firstRequest = JsonDocument.Parse(await first.ReadWrittenLineAsync()).RootElement;
        await first.FeedLineAsync(VerifiedPayload(firstRequest.GetProperty("id").GetInt32()));
        await firstRead;
        await reader.ResetConnectionAsync(CancellationToken.None);

        var secondRead = reader.ReadAsync(CancellationToken.None);
        await CompleteInitializeAsync(second);
        var secondRequest = JsonDocument.Parse(await second.ReadWrittenLineAsync()).RootElement;
        await second.FeedLineAsync(VerifiedPayload(secondRequest.GetProperty("id").GetInt32()));
        await secondRead;

        Assert.Equal(true, first.IsDisposed);
        Assert.Equal(2, factory.CreationCount);
    }

    private static async Task CompleteInitializeAsync(ScriptedAppServerConnection connection)
    {
        var initialize = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        await connection.FeedLineAsync($"{{\"id\":{initialize.GetProperty("id").GetInt32()},\"result\":{{\"userAgent\":\"fake\"}}}}");
        var initialized = JsonDocument.Parse(await connection.ReadWrittenLineAsync()).RootElement;
        Assert.Equal("initialized", initialized.GetProperty("method").GetString());
    }

    private static string VerifiedPayload(int id) => JsonSerializer.Serialize(new
    {
        id,
        result = new
        {
            rateLimits = PoolPayload(1),
            rateLimitsByLimitId = new Dictionary<string, object>
            {
                ["codex"] = PoolPayload(1),
            },
        },
    });

    private static object PoolPayload(int usedPercent) => new
    {
        limitId = "codex",
        primary = new { usedPercent, windowDurationMins = 10_080, resetsAt = 1_787_132_210L },
        secondary = (object?)null,
    };
}
