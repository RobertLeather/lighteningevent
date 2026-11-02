public interface IStrikePublisher
{
    Task PublishAsync(string json, CancellationToken cancellationToken);
}

public sealed class PublishLoop
{
    private readonly IStrikeSampler _sampler;
    private readonly IStrikePublisher _publisher;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string> _log;

    public PublishLoop(
        IStrikeSampler sampler,
        IStrikePublisher publisher,
        Func<TimeSpan, CancellationToken, Task> delay,
        Action<string> log)
    {
        _sampler = sampler;
        _publisher = publisher;
        _delay = delay;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!await WaitAsync(TimeSpan.FromMilliseconds(_sampler.NextDelayMilliseconds()), cancellationToken))
            {
                break;
            }

            var draw = _sampler.NextStrike();
            if (!StrikePayload.TryBuild(draw.Latitude, draw.Longitude, draw.Intensity, out var json))
            {
                _log("Rejected a strike outside the contract.");
                continue;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _publisher.PublishAsync(json, cancellationToken);
                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _log($"Kafka publish failed: {exception.Message}");
                    var seconds = _sampler.NextRetrySeconds();
                    if (!await WaitAsync(TimeSpan.FromSeconds(seconds), cancellationToken))
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        try
        {
            await _delay(delay, cancellationToken);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
