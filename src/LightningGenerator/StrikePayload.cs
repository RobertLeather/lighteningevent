using System.Text;
using System.Text.Json;

public static class StrikePayload
{
    public static bool TryBuild(double latitude, double longitude, double intensity, out string json)
    {
        json = "";
        if (!IsCoordinate(latitude, StrikeBounds.LatitudeMin, StrikeBounds.LatitudeMax))
        {
            return false;
        }

        if (!IsCoordinate(longitude, StrikeBounds.LongitudeMin, StrikeBounds.LongitudeMax))
        {
            return false;
        }

        if (!IsWholeIntensity(intensity, out var intensityValue))
        {
            return false;
        }

        json = Write(latitude, longitude, intensityValue);
        return true;
    }

    public static async Task<bool> PublishIfValidAsync(
        IStrikePublisher publisher,
        double latitude,
        double longitude,
        double intensity,
        CancellationToken cancellationToken)
    {
        if (!TryBuild(latitude, longitude, intensity, out var json))
        {
            return false;
        }

        await publisher.PublishAsync(json, cancellationToken);
        return true;
    }

    private static bool IsCoordinate(double value, double min, double max)
        => !double.IsNaN(value) && !double.IsInfinity(value) && value >= min && value <= max;

    private static bool IsWholeIntensity(double intensity, out int value)
    {
        value = 0;
        if (double.IsNaN(intensity) || double.IsInfinity(intensity))
        {
            return false;
        }

        if (intensity < StrikeBounds.IntensityMin || intensity > StrikeBounds.IntensityMax)
        {
            return false;
        }

        if (intensity != Math.Truncate(intensity))
        {
            return false;
        }

        value = (int)intensity;
        return value >= StrikeBounds.IntensityMin && value <= StrikeBounds.IntensityMax;
    }

    private static string Write(double latitude, double longitude, int intensity)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteNumber("latitude", latitude);
            writer.WriteNumber("longitude", longitude);
            writer.WriteNumber("intensity", intensity);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
