public sealed record WebSettings(
    string MongoConnectionString,
    string Database,
    string Collection,
    string MapsApiKey)
{
    public static WebSettings From(Func<string, string?> read)
    {
        return new WebSettings(
            Value(read, "MONGODB_CONNECTION_STRING", "mongodb://localhost:27017"),
            Value(read, "MONGODB_DATABASE", "Lightening"),
            Value(read, "MONGODB_COLLECTION", "LighteningEvents"),
            Value(read, "GOOGLE_MAPS_API_KEY", "AIzaSyBei-BtlTgw2gfyi0NJk9yl4_PEWI7fK6U"));
    }

    private static string Value(Func<string, string?> read, string name, string fallback)
    {
        var value = read(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}

public sealed class MapOptions
{
    public string ApiKey { get; init; } = "";
}
