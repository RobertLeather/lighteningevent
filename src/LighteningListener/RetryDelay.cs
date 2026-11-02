public static class RetryDelay
{
    public static int NextSeconds(Random random)
        => random.Next(StrikeBounds.RetryMinSeconds, StrikeBounds.RetryMaxSeconds + 1);
}
