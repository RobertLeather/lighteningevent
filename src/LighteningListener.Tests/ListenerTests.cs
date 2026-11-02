using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;

public class ListenerTests
{
    private static readonly DateTimeOffset FixedClock = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid FixedId = Guid.Parse("3f1c0a2e-7b4d-4e1a-8c3d-9a0b1c2d3e4f");
    private const string ValidJson = """{"latitude":51.5074,"longitude":-0.1278,"intensity":7}""";

    [Fact]
    public void Consumer_is_configured_with_the_topic_and_group_and_does_not_auto_commit()
    {
        var settings = ListenerSettings.From(_ => null);
        var config = KafkaConsumers.CreateConfig(settings);
        Assert.Equal("lightening-events", settings.Topic);
        Assert.Equal("lightening-listener", settings.GroupId);
        Assert.Equal("lightening-listener", config.GroupId);
        Assert.Equal("localhost:9092", config.BootstrapServers);
        Assert.False(config.EnableAutoCommit);

        var root = RepoRoot();
        var source = string.Join('\n', Directory.GetFiles(Path.Combine(root, "src", "LighteningListener"), "*.cs").Select(File.ReadAllText));
        Assert.Contains("ConsumerBuilder", source);
        Assert.Contains("Subscribe", source);
        Assert.DoesNotContain("ProducerBuilder", source);
        Assert.DoesNotContain("IProducer", source);
    }

    [Fact]
    public void Document_copies_the_message_and_sets_id_timestamp_and_expiry()
    {
        Assert.True(StrikeMessageParser.TryParse(ValidJson, out var message, out _));
        var document = StrikeDocumentFactory.Create(message, FixedClock, FixedId);
        Assert.Equal(51.5074, document.Latitude);
        Assert.Equal(-0.1278, document.Longitude);
        Assert.Equal(7, document.Intensity);
        Assert.Equal("3f1c0a2e-7b4d-4e1a-8c3d-9a0b1c2d3e4f", document.ID);
        Assert.Equal(document.ID, document.Id);
        Assert.Matches(UuidPattern(), document.ID);
        Assert.Equal(FixedClock.UtcDateTime, document.Timestamp);
        Assert.Equal(new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc), document.Expiry);
        Assert.Equal(72, StrikeBounds.LifetimeHours);
    }

    [Fact]
    public void A_new_id_is_a_lowercase_uuid_version_4()
    {
        var document = StrikeDocumentFactory.Create(new StrikeMessage(1, 2, 3), FixedClock, Guid.NewGuid());
        Assert.Matches(UuidPattern(), document.ID);
        Assert.Equal(document.ID, document.Id);
    }

    [Fact]
    public async Task Insert_uses_the_driver_asynchronous_insert()
    {
        var collection = new Mock<IMongoCollection<BsonDocument>>();
        collection
            .Setup(candidate => candidate.InsertOneAsync(
                It.IsAny<BsonDocument>(),
                It.IsAny<InsertOneOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var store = new MongoStrikeStore(collection.Object);
        var document = StrikeDocumentFactory.Create(new StrikeMessage(51.5074, -0.1278, 7), FixedClock, FixedId);

        await store.InsertAsync(document, CancellationToken.None);

        collection.Verify(candidate => candidate.InsertOneAsync(
            It.Is<BsonDocument>(bson =>
                bson["_id"] == bson["ID"]
                && bson["ID"] == "3f1c0a2e-7b4d-4e1a-8c3d-9a0b1c2d3e4f"
                && bson["latitude"] == 51.5074
                && bson["longitude"] == -0.1278
                && bson["intensity"] == 7),
            It.IsAny<InsertOneOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancelled_token_ends_the_consume_loop()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var source = new ScriptedSource(cancellation);
        var loop = Loop(source, new ScriptedStore(), new List<string>(), new List<TimeSpan>());
        await loop.RunAsync(cancellation.Token);
        Assert.Empty(source.Committed);

        var code = await ProcessHost.RunUntilStoppedAsync(
            _ => throw new OperationCanceledException(),
            new CancellationToken(canceled: true));
        Assert.Equal(0, code);
    }

    [Theory]
    [InlineData("""{"latitude":91,"longitude":0,"intensity":1}""")]
    [InlineData("""{"latitude":0,"longitude":-181,"intensity":1}""")]
    [InlineData("""{"latitude":0,"longitude":0,"intensity":0}""")]
    [InlineData("""{"latitude":0,"longitude":0,"intensity":11}""")]
    [InlineData("""{"latitude":0,"longitude":0,"intensity":7.5}""")]
    [InlineData("not-json")]
    [InlineData("""{"latitude":0,"longitude":0,"intensity":1,"note":"x"}""")]
    [InlineData("""{"latitude":0,"longitude":0}""")]
    public async Task Invalid_message_is_skipped_and_the_next_valid_message_is_stored(string invalid)
    {
        using var cancellation = new CancellationTokenSource();
        var logs = new List<string>();
        var source = new ScriptedSource(cancellation, invalid, ValidJson);
        var store = new ScriptedStore();
        var loop = Loop(source, store, logs, new List<TimeSpan>());

        await loop.RunAsync(cancellation.Token);

        Assert.Equal(new[] { invalid, ValidJson }, source.Committed);
        var stored = Assert.Single(store.Documents);
        Assert.Equal(51.5074, stored.Latitude);
        Assert.Equal(-0.1278, stored.Longitude);
        Assert.Equal(7, stored.Intensity);
        Assert.NotEmpty(logs);
    }

    [Fact]
    public async Task Successful_insert_commits_and_a_failed_insert_does_not()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new ScriptedSource(cancellation, ValidJson);
        var store = new ScriptedStore();
        var events = new List<string>();
        store.OnInsert = document => events.Add("insert:" + document.ID);
        source.OnCommit = value => events.Add("commit");
        var loop = Loop(source, store, new List<string>(), new List<TimeSpan>());

        await loop.RunAsync(cancellation.Token);

        Assert.Equal(new[] { "insert:" + FixedId.ToString("D"), "commit" }, events);
    }

    [Fact]
    public async Task Mongo_failure_does_not_commit_until_a_later_insert_succeeds()
    {
        using var cancellation = new CancellationTokenSource();
        var logs = new List<string>();
        var delays = new List<TimeSpan>();
        var source = new ScriptedSource(cancellation, ValidJson);
        var store = new ScriptedStore { FailuresRemaining = 1 };
        var loop = Loop(source, store, logs, delays);

        await loop.RunAsync(cancellation.Token);

        Assert.Equal(ValidJson, Assert.Single(source.Committed));
        Assert.Single(store.Documents);
        Assert.NotEmpty(logs);
        Assert.InRange(delays[0].TotalSeconds, StrikeBounds.RetryMinSeconds, StrikeBounds.RetryMaxSeconds);
    }

    [Fact]
    public void Retry_draws_stay_inside_1_to_30_seconds()
    {
        var random = new Random(4);
        for (var i = 0; i < 10000; i++)
        {
            Assert.InRange(RetryDelay.NextSeconds(random), 1, 30);
        }

        Assert.Equal(1, StrikeBounds.RetryMinSeconds);
        Assert.Equal(30, StrikeBounds.RetryMaxSeconds);
    }

    private static ConsumeLoop Loop(
        ScriptedSource source,
        ScriptedStore store,
        List<string> logs,
        List<TimeSpan> delays)
    {
        var random = new Random(6);
        return new ConsumeLoop(
            source,
            store,
            () => FixedClock,
            () => FixedId,
            () => RetryDelay.NextSeconds(random),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            logs.Add);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Lightning.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Lightning.sln was not found.");
    }

    private static Regex UuidPattern()
        => new("^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$", RegexOptions.CultureInvariant);

    private sealed class ScriptedSource : IMessageSource
    {
        private readonly CancellationTokenSource _cancellation;
        private readonly Queue<string> _messages;

        public ScriptedSource(CancellationTokenSource cancellation, params string[] messages)
        {
            _cancellation = cancellation;
            _messages = new Queue<string>(messages);
        }

        public List<string> Committed { get; } = new();

        public Action<string>? OnCommit { get; set; }

        public Task<IncomingMessage?> ReadAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_messages.Count == 0)
            {
                _cancellation.Cancel();
                throw new OperationCanceledException(cancellationToken);
            }

            var value = _messages.Dequeue();
            return Task.FromResult<IncomingMessage?>(new IncomingMessage(value, value));
        }

        public Task CommitAsync(IncomingMessage message, CancellationToken cancellationToken)
        {
            Committed.Add(message.Value);
            OnCommit?.Invoke(message.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedStore : IStrikeStore
    {
        public int FailuresRemaining { get; set; }

        public List<StrikeDocument> Documents { get; } = new();

        public Action<StrikeDocument>? OnInsert { get; set; }

        public Task InsertAsync(StrikeDocument document, CancellationToken cancellationToken)
        {
            if (FailuresRemaining > 0)
            {
                FailuresRemaining--;
                throw new InvalidOperationException("mongodb down");
            }

            Documents.Add(document);
            OnInsert?.Invoke(document);
            return Task.CompletedTask;
        }
    }
}
