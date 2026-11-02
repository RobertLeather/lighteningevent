using System.Text.Json;

public readonly record struct StrikeMessage(double Latitude, double Longitude, int Intensity);

public static class StrikeMessageParser
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "latitude",
        "longitude",
        "intensity"
    };

    public static bool TryParse(string? json, out StrikeMessage message, out string error)
    {
        message = default;
        if (string.IsNullOrWhiteSpace(json))
        {
            error = "Message is empty.";
            return false;
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            error = $"Message is not valid JSON: {exception.Message}";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "Message is not a JSON object.";
                return false;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    error = "Message contains a duplicate field.";
                    return false;
                }
            }

            if (!names.SetEquals(Fields))
            {
                error = "Message fields must be latitude, longitude, and intensity.";
                return false;
            }

            if (!TryCoordinate(document.RootElement.GetProperty("latitude"), StrikeBounds.LatitudeMin, StrikeBounds.LatitudeMax, out var latitude))
            {
                error = "Latitude is outside -90 through 90.";
                return false;
            }

            if (!TryCoordinate(document.RootElement.GetProperty("longitude"), StrikeBounds.LongitudeMin, StrikeBounds.LongitudeMax, out var longitude))
            {
                error = "Longitude is outside -180 through 180.";
                return false;
            }

            if (!TryIntensity(document.RootElement.GetProperty("intensity"), out var intensity))
            {
                error = "Intensity is not an integer from 1 through 10.";
                return false;
            }

            message = new StrikeMessage(latitude, longitude, intensity);
            error = "";
            return true;
        }
    }

    private static bool TryCoordinate(JsonElement element, double min, double max, out double value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetDouble(out value))
        {
            return false;
        }

        return !double.IsNaN(value) && value >= min && value <= max;
    }

    private static bool TryIntensity(JsonElement element, out int value)
    {
        value = 0;
        if (element.ValueKind != JsonValueKind.Number)
        {
            return false;
        }

        var raw = element.GetRawText();
        if (raw.Contains('.') || raw.Contains('e') || raw.Contains('E') || raw.Contains('+'))
        {
            return false;
        }

        return element.TryGetInt32(out value)
            && value >= StrikeBounds.IntensityMin
            && value <= StrikeBounds.IntensityMax;
    }
}
