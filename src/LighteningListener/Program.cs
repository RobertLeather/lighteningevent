using System.Runtime.InteropServices;
using Confluent.Kafka;
using MongoDB.Bson;
using MongoDB.Driver;

var settings = ListenerSettings.FromEnvironment();
using var cancellation = new CancellationTokenSource();

void Stop() => cancellation.Cancel();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    Stop();
};

using var sigint = PosixSignalRegistration.Create(PosixSignal.SIGINT, _ => Stop());
using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, _ => Stop());

using var consumer = new ConsumerBuilder<Ignore, string>(KafkaConsumers.CreateConfig(settings))
    .SetErrorHandler((_, error) => Console.Error.WriteLine(error.Reason))
    .Build();
var source = new KafkaMessageSource(consumer, settings.Topic);
var database = new MongoClient(settings.MongoConnectionString).GetDatabase(settings.Database);
var collection = database.GetCollection<BsonDocument>(settings.Collection);
var store = new MongoStrikeStore(collection);
var loop = new ConsumeLoop(
    source,
    store,
    static () => DateTimeOffset.UtcNow,
    Guid.NewGuid,
    () => RetryDelay.NextSeconds(Random.Shared),
    Task.Delay,
    static message => Console.Error.WriteLine(message));

var exitCode = await ProcessHost.RunUntilStoppedAsync(loop.RunAsync, cancellation.Token);
source.Close();
return exitCode;
