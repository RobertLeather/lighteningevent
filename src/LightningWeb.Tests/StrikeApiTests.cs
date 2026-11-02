using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

public class StrikeApiTests : IClassFixture<LightningWebFactory>
{
    private readonly LightningWebFactory _factory;

    public StrikeApiTests(LightningWebFactory factory)
    {
        _factory = factory;
        _factory.Strikes.Docs.Clear();
        _factory.Strikes.Fail = false;
    }

    [Fact]
    public async Task Page_includes_the_map_poll_and_missing_key_message()
    {
        var html = await _factory.CreateClient().GetStringAsync("/");
        Assert.Contains("lang=\"en\"", html);
        Assert.Equal(1, Count(html, "<h1"));
        Assert.Equal(1, Count(html, "<title"));
        Assert.Contains("width=device-width, initial-scale=1", html);
        Assert.Contains("Skip to strike list", html);
        Assert.Contains("World map of lightning strikes", html);
        Assert.Contains("Current lightning strikes", html);
        Assert.Contains("id=\"map\"", html);
        Assert.Contains("/api/strikes", html);
        Assert.Contains("10000", html);
        Assert.Contains("GOOGLE_MAPS_API_KEY is not set", html);
        Assert.DoesNotMatch("key=[A-Za-z0-9]", html);
        Assert.Contains("Pause updates", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.DoesNotContain("mongodb", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MONGODB_CONNECTION_STRING", html);
    }

    [Fact]
    public async Task Configured_map_key_is_placed_in_the_script_url()
    {
        await using var factory = new LightningWebFactory { MapsApiKey = "unit-test-map" };
        var html = await factory.CreateClient().GetStringAsync("/");
        Assert.Contains("key=unit-test-map", html);
        Assert.DoesNotContain("GOOGLE_MAPS_API_KEY is not set", html);
        Assert.DoesNotContain("mongodb", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Current_strikes_are_returned_newest_first_and_expired_ones_are_omitted()
    {
        var clock = _factory.Clock.GetUtcNow().UtcDateTime;
        var earlier = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var later = new DateTime(2026, 11, 2, 13, 0, 0, DateTimeKind.Utc);
        _factory.Strikes.Docs.Add(new StoredStrike("early-id", 51.5074, -0.1278, 7, earlier, clock.AddHours(1)));
        _factory.Strikes.Docs.Add(new StoredStrike("late-id", 10, 20, 4, later, clock.AddHours(1)));
        _factory.Strikes.Docs.Add(new StoredStrike("past-id", 1, 2, 3, earlier, clock.AddHours(-1)));
        _factory.Strikes.Docs.Add(new StoredStrike("exact-id", 1, 2, 3, earlier, clock));

        var body = await _factory.CreateClient().GetStringAsync("/api/strikes");
        using var document = JsonDocument.Parse(body);
        var strikes = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, strikes.Length);
        Assert.Equal("late-id", strikes[0].GetProperty("id").GetString());
        Assert.Equal("2026-11-02T13:00:00.000Z", strikes[0].GetProperty("timestamp").GetString());
        Assert.Equal("early-id", strikes[1].GetProperty("id").GetString());
        Assert.Equal(
            new[] { "id", "latitude", "longitude", "intensity", "timestamp", "expiry" },
            strikes[1].EnumerateObject().Select(property => property.Name).ToArray());
        Assert.Equal(51.5074, strikes[1].GetProperty("latitude").GetDouble());
        Assert.Equal(-0.1278, strikes[1].GetProperty("longitude").GetDouble());
        Assert.Equal(7, strikes[1].GetProperty("intensity").GetInt32());
        Assert.DoesNotContain("mongodb", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Mongo_read_failure_returns_503_and_health_stays_ok()
    {
        _factory.Strikes.Fail = true;
        var client = _factory.CreateClient();
        var strikes = await client.GetAsync("/api/strikes");
        var health = await client.GetAsync("/health");
        var body = await strikes.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, strikes.StatusCode);
        Assert.Equal("strikes-unavailable", document.RootElement.GetProperty("error").GetString());
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("ok", await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Mongo_filter_keeps_expiry_strictly_after_now_and_maps_the_document()
    {
        var now = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);
        var registry = BsonSerializer.SerializerRegistry;
        var serializer = registry.GetSerializer<BsonDocument>();
        var rendered = MongoStrikeDocuments.CurrentFilter(now).Render(new RenderArgs<BsonDocument>(serializer, registry));
        var text = rendered.ToString();
        Assert.Contains("expiry", text);
        Assert.Contains("$gt", text);
        Assert.False(MongoStrikeDocuments.IsCurrent(now, now));
        Assert.False(MongoStrikeDocuments.IsCurrent(now.AddHours(-1), now));
        Assert.True(MongoStrikeDocuments.IsCurrent(now.AddHours(1), now));

        var mapped = MongoStrikeDocuments.MapDocument(new BsonDocument
        {
            { "_id", "3f1c0a2e-7b4d-4e1a-8c3d-9a0b1c2d3e4f" },
            { "ID", "3f1c0a2e-7b4d-4e1a-8c3d-9a0b1c2d3e4f" },
            { "latitude", 51.5074 },
            { "longitude", -0.1278 },
            { "intensity", 7 },
            { "timestamp", new BsonDateTime(now) },
            { "expiry", new BsonDateTime(now.AddHours(72)) }
        });
        Assert.Equal("3f1c0a2e-7b4d-4e1a-8c3d-9a0b1c2d3e4f", mapped.Id);
        Assert.Equal(7, mapped.Intensity);
    }

    private static int Count(string text, string token)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += token.Length;
        }

        return count;
    }
}
