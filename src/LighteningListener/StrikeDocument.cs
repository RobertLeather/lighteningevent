public sealed class StrikeDocument
{
    public required string Id { get; init; }

    public required string ID { get; init; }

    public required double Latitude { get; init; }

    public required double Longitude { get; init; }

    public required int Intensity { get; init; }

    public required DateTime Timestamp { get; init; }

    public required DateTime Expiry { get; init; }
}
