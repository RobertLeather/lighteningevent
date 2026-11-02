public static class StrikeDocumentFactory
{
    public static StrikeDocument Create(StrikeMessage message, DateTimeOffset now, Guid id)
    {
        var timestamp = now.UtcDateTime;
        var expiry = timestamp.AddHours(StrikeBounds.LifetimeHours);
        var idText = id.ToString("D");
        return new StrikeDocument
        {
            Id = idText,
            ID = idText,
            Latitude = message.Latitude,
            Longitude = message.Longitude,
            Intensity = message.Intensity,
            Timestamp = timestamp,
            Expiry = expiry
        };
    }
}
