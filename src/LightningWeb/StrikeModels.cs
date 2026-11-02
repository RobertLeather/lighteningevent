using System.Text.Json.Serialization;

public sealed record StoredStrike(
    string Id,
    double Latitude,
    double Longitude,
    int Intensity,
    DateTime Timestamp,
    DateTime Expiry);

public sealed record StrikeDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("latitude")] double Latitude,
    [property: JsonPropertyName("longitude")] double Longitude,
    [property: JsonPropertyName("intensity")] int Intensity,
    [property: JsonPropertyName("timestamp")] DateTime Timestamp,
    [property: JsonPropertyName("expiry")] DateTime Expiry)
{
    public static StrikeDto From(StoredStrike strike)
        => new(strike.Id, strike.Latitude, strike.Longitude, strike.Intensity, strike.Timestamp, strike.Expiry);
}

public sealed record ErrorResponse([property: JsonPropertyName("error")] string Error);

public interface IStrikeDocuments
{
    Task<IReadOnlyList<StoredStrike>> GetUnexpiredAsync(DateTime utcNow, CancellationToken cancellationToken);
}
