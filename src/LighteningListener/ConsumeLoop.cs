public sealed record IncomingMessage(string Value, object Handle);

public interface IMessageSource
{
    Task<IncomingMessage?> ReadAsync(CancellationToken cancellationToken);

    Task CommitAsync(IncomingMessage message, CancellationToken cancellationToken);
}

public interface IStrikeStore
{
    Task InsertAsync(StrikeDocument document, CancellationToken cancellationToken);
}

public sealed class ConsumeLoop
{
    private readonly IMessageSource _source;
    private readonly IStrikeStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<Guid> _ids;
    private readonly Func<int> _retrySeconds;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Action<string> _log;

    public ConsumeLoop(
        IMessageSource source,
        IStrikeStore store,
        Func<DateTimeOffset> clock,
        Func<Guid> ids,
        Func<int> retrySeconds,
        Func<TimeSpan, CancellationToken, Task> delay,
        Action<string> log)
    {
        _source = source;
        _store = store;
        _clock = clock;
        _ids = ids;
        _retrySeconds = retrySeconds;
        _delay = delay;
        _log = log;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            IncomingMessage? message;
            try
            {
                message = await _source.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (message is null)
            {
                continue;
            }

            if (!StrikeMessageParser.TryParse(message.Value, out var strike, out var error))
            {
                _log(error);
                if (!await CommitAsync(message, cancellationToken))
                {
                    break;
                }

                continue;
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var document = StrikeDocumentFactory.Create(strike, _clock(), _ids());
                    await _store.InsertAsync(document, cancellationToken);
                    if (!await CommitAsync(message, cancellationToken))
                    {
                        return;
                    }

                    break;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    _log($"MongoDB insert failed: {exception.Message}");
                    var seconds = _retrySeconds();
                    if (!await WaitAsync(TimeSpan.FromSeconds(seconds), cancellationToken))
                    {
                        return;
                    }
                }
            }
        }
    }

    private async Task<bool> CommitAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await _source.CommitAsync(message, cancellationToken);
                return true;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception exception)
            {
                _log($"Offset commit failed: {exception.Message}");
                var seconds = _retrySeconds();
                if (!await WaitAsync(TimeSpan.FromSeconds(seconds), cancellationToken))
                {
                    return false;
                }
            }
        }

        return false;
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
