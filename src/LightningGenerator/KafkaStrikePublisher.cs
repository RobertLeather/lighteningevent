using Confluent.Kafka;

public sealed class KafkaStrikePublisher : IStrikePublisher
{
    private readonly IProducer<Null, string> _producer;
    private readonly string _topic;

    public KafkaStrikePublisher(IProducer<Null, string> producer, string topic)
    {
        _producer = producer;
        _topic = topic;
    }

    public async Task PublishAsync(string json, CancellationToken cancellationToken)
    {
        var message = new Message<Null, string> { Value = json };
        await _producer.ProduceAsync(_topic, message, cancellationToken);
    }
}
