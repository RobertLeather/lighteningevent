public readonly record struct StrikeDraw(double Latitude, double Longitude, int Intensity);

public interface IStrikeSampler
{
    StrikeDraw NextStrike();

    int NextDelayMilliseconds();

    int NextRetrySeconds();
}

public sealed class StrikeSampler : IStrikeSampler
{
    private readonly Random _random;

    public StrikeSampler(int seed)
        : this(new Random(seed))
    {
    }

    public StrikeSampler(Random random)
    {
        _random = random;
    }

    public StrikeDraw NextStrike()
    {
        var latitude = NextCoordinate(StrikeBounds.LatitudeMin, StrikeBounds.LatitudeMax);
        var longitude = NextCoordinate(StrikeBounds.LongitudeMin, StrikeBounds.LongitudeMax);
        var intensity = _random.Next(StrikeBounds.IntensityMin, StrikeBounds.IntensityMax + 1);
        return new StrikeDraw(latitude, longitude, intensity);
    }

    public int NextDelayMilliseconds()
        => _random.Next(StrikeBounds.DelayMinMilliseconds, StrikeBounds.DelayMaxMilliseconds + 1);

    public int NextRetrySeconds()
        => _random.Next(StrikeBounds.RetryMinSeconds, StrikeBounds.RetryMaxSeconds + 1);

    private double NextCoordinate(double min, double max)
    {
        var unit = _random.NextDouble();
        var value = unit == 0d ? max : min + (unit * (max - min));
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
