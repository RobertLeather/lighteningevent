using MongoDB.Bson;
using MongoDB.Driver;

var builder = WebApplication.CreateBuilder(args);
var settings = WebSettings.From(name => builder.Configuration[name]);
builder.Services.AddSingleton(new MapOptions { ApiKey = settings.MapsApiKey });
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IStrikeDocuments>(_ =>
{
    var client = new MongoClient(settings.MongoConnectionString);
    var collection = client.GetDatabase(settings.Database).GetCollection<BsonDocument>(settings.Collection);
    return new MongoStrikeDocuments(collection);
});

var app = builder.Build();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Text("ok"));

app.MapGet("/api/strikes", async (
    IStrikeDocuments documents,
    TimeProvider clock,
    ILoggerFactory loggerFactory,
    CancellationToken cancellationToken) =>
{
    try
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var strikes = (await documents.GetUnexpiredAsync(now, cancellationToken))
            .OrderByDescending(strike => strike.Timestamp)
            .Select(StrikeDto.From)
            .ToList();
        return Results.Json(strikes, WebJson.Options);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        throw;
    }
    catch (Exception exception)
    {
        loggerFactory.CreateLogger("Strikes").LogError(exception, "MongoDB read failed.");
        return Results.Json(
            new ErrorResponse("strikes-unavailable"),
            WebJson.Options,
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapGet("/", (IWebHostEnvironment environment, MapOptions maps) =>
{
    var html = File.ReadAllText(LocatePage(environment));
    string status;
    string script;
    if (string.IsNullOrEmpty(maps.ApiKey))
    {
        status = "<p id=\"map-key-status\">GOOGLE_MAPS_API_KEY is not set</p>";
        script = "";
    }
    else
    {
        status = "";
        var key = Uri.EscapeDataString(maps.ApiKey);
        script = $"<script defer src=\"https://maps.googleapis.com/maps/api/js?key={key}&amp;callback=initMap\"></script>";
    }

    html = html.Replace("{{MAP_STATUS}}", status, StringComparison.Ordinal);
    html = html.Replace("{{MAP_SCRIPT}}", script, StringComparison.Ordinal);
    return Results.Content(html, "text/html; charset=utf-8");
});

app.Run();

static string LocatePage(IWebHostEnvironment environment)
{
    var candidates = new[]
    {
        Path.Combine(environment.ContentRootPath, "content", "index.html"),
        Path.Combine(AppContext.BaseDirectory, "content", "index.html")
    };
    foreach (var candidate in candidates)
    {
        if (File.Exists(candidate))
        {
            return candidate;
        }
    }

    return candidates[0];
}

public partial class Program
{
}
