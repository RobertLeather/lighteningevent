using Confluent.Kafka;

public static class KafkaConsumers
{
    public static ConsumerConfig CreateConfig(ListenerSettings settings)
    {
        return new ConsumerConfig
        {
            BootstrapServers = settings.BootstrapServers,
            GroupId = settings.GroupId,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest
        };
    }
}

public sealed class KafkaMessageSource : IMessageSource
{
    private readonly IConsumer<Ignore, string> _consumer;

    public KafkaMessageSource(IConsumer<Ignore, string> consumer, string topic)
    {
        _consumer = consumer;
        _consumer.Subscribe(topic);
    }

    public Task<IncomingMessage?> ReadAsync(CancellationToken cancellationToken)
    {
        var result = _consumer.Consume(cancellationToken);
        var value = result?.Message?.Value ?? "";
        return Task.FromResult<IncomingMessage?>(result is null ? null : new IncomingMessage(value, result));
    }

    public Task CommitAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _consumer.Commit((ConsumeResult<Ignore, string>)message.Handle);
        return Task.CompletedTask;
    }

    public void Close() => _consumer.Close();
}
