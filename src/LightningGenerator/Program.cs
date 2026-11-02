using System.Runtime.InteropServices;
using Confluent.Kafka;

var settings = GeneratorSettings.FromEnvironment();
using var cancellation = new CancellationTokenSource();

void Stop() => cancellation.Cancel();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Stop();
};

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => Stop());
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Stop());

var config = new ProducerConfig
{
    BootstrapServers = settings.BootstrapServers,
    Acks = Acks.All,
    MessageTimeoutMs = 15000,
    SocketTimeoutMs = 10000
};

using var producer = new ProducerBuilder<Null, string>(config).Build();
var publisher = new KafkaStrikePublisher(producer, settings.Topic);
var loop = new PublishLoop(
    new StrikeSampler(Random.Shared),
    publisher,
    Task.Delay,
    static message => Console.Error.WriteLine(message));

var exitCode = await ProcessHost.RunUntilStoppedAsync(loop.RunAsync, cancellation.Token);
try
{
    producer.Flush(TimeSpan.FromSeconds(2));
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
}

return exitCode;
