using System.Text.Json;

public class GeneratorTests
{
    [Fact]
    public void Bounds_match_the_named_constants()
    {
        Assert.Equal(-90, StrikeBounds.LatitudeMin);
        Assert.Equal(90, StrikeBounds.LatitudeMax);
        Assert.Equal(-180, StrikeBounds.LongitudeMin);
        Assert.Equal(180, StrikeBounds.LongitudeMax);
        Assert.Equal(1, StrikeBounds.IntensityMin);
        Assert.Equal(10, StrikeBounds.IntensityMax);
        Assert.Equal(50, StrikeBounds.DelayMinMilliseconds);
        Assert.Equal(120000, StrikeBounds.DelayMaxMilliseconds);
        Assert.Equal(1, StrikeBounds.RetryMinSeconds);
        Assert.Equal(30, StrikeBounds.RetryMaxSeconds);
    }

    [Fact]
    public void Seeded_strikes_stay_inside_the_closed_ranges()
    {
        var sampler = new StrikeSampler(42);
        for (var i = 0; i < 10000; i++)
        {
            var draw = sampler.NextStrike();
            Assert.InRange(draw.Latitude, StrikeBounds.LatitudeMin, StrikeBounds.LatitudeMax);
            Assert.InRange(draw.Longitude, StrikeBounds.LongitudeMin, StrikeBounds.LongitudeMax);
            Assert.InRange(draw.Intensity, StrikeBounds.IntensityMin, StrikeBounds.IntensityMax);
            Assert.Equal(draw.Intensity, (int)Math.Truncate((double)draw.Intensity));
        }
    }

    [Fact]
    public void The_same_seed_reproduces_strikes_and_a_different_seed_does_not()
    {
        var first = DrawStrikes(7, 64);
        var second = DrawStrikes(7, 64);
        var other = DrawStrikes(8, 64);
        Assert.Equal(first, second);
        Assert.NotEqual(first, other);
    }

    [Fact]
    public void Seeded_delays_stay_inside_the_closed_range_and_repeat_for_the_same_seed()
    {
        var sampler = new StrikeSampler(11);
        for (var i = 0; i < 10000; i++)
        {
            Assert.InRange(sampler.NextDelayMilliseconds(), StrikeBounds.DelayMinMilliseconds, StrikeBounds.DelayMaxMilliseconds);
        }

        Assert.Equal(Delays(3, 32), Delays(3, 32));
        Assert.NotEqual(Delays(3, 32), Delays(4, 32));
    }

    [Fact]
    public void Seeded_retry_waits_stay_inside_1_to_30_seconds()
    {
        var sampler = new StrikeSampler(9);
        for (var i = 0; i < 10000; i++)
        {
            Assert.InRange(sampler.NextRetrySeconds(), StrikeBounds.RetryMinSeconds, StrikeBounds.RetryMaxSeconds);
        }
    }

    [Fact]
    public void Example_payload_has_the_three_contract_fields()
    {
        Assert.True(StrikePayload.TryBuild(51.5074, -0.1278, 7, out var json));
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        Assert.Equal(new[] { "latitude", "longitude", "intensity" }, names);
        Assert.Equal(51.5074, document.RootElement.GetProperty("latitude").GetDouble());
        Assert.Equal(-0.1278, document.RootElement.GetProperty("longitude").GetDouble());
        Assert.Equal(7, document.RootElement.GetProperty("intensity").GetInt32());
        Assert.Equal("7", document.RootElement.GetProperty("intensity").GetRawText());
    }

    [Theory]
    [InlineData(90, 0, 1)]
    [InlineData(-90, 0, 10)]
    [InlineData(0, 180, 1)]
    [InlineData(0, -180, 10)]
    public void Endpoint_coordinates_and_intensity_are_accepted(double latitude, double longitude, int intensity)
    {
        Assert.True(StrikePayload.TryBuild(latitude, longitude, intensity, out var json));
        using var document = JsonDocument.Parse(json);
        Assert.Equal(intensity, document.RootElement.GetProperty("intensity").GetInt32());
    }

    [Theory]
    [InlineData(90.0001, 0, 1)]
    [InlineData(0, 180.0001, 1)]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 11)]
    [InlineData(0, 0, 7.5)]
    public async Task Rejected_values_are_not_published(double latitude, double longitude, double intensity)
    {
        var publisher = new RecordingPublisher();
        var published = await StrikePayload.PublishIfValidAsync(publisher, latitude, longitude, intensity, CancellationToken.None);
        Assert.False(published);
        Assert.Empty(publisher.Payloads);
    }

    [Fact]
    public async Task Cancelled_token_ends_the_loop()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var publisher = new RecordingPublisher();
        var loop = new PublishLoop(new StrikeSampler(1), publisher, (_, _) => Task.CompletedTask, _ => { });
        await loop.RunAsync(cancellation.Token);
        Assert.Empty(publisher.Payloads);

        var code = await ProcessHost.RunUntilStoppedAsync(
            _ => throw new OperationCanceledException(),
            new CancellationToken(canceled: true));
        Assert.Equal(0, code);
    }

    [Fact]
    public async Task Loop_publishes_until_it_is_cancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var publishes = 0;
        var publisher = new RecordingPublisher(_ =>
        {
            publishes++;
            if (publishes >= 2)
            {
                cancellation.Cancel();
            }

            return Task.CompletedTask;
        });
        var loop = new PublishLoop(new StrikeSampler(2), publisher, (_, _) => Task.CompletedTask, _ => { });
        await loop.RunAsync(cancellation.Token);
        Assert.Equal(2, publishes);
    }

    [Fact]
    public async Task Failed_publish_stays_in_the_loop_and_retries_within_1_to_30_seconds()
    {
        using var cancellation = new CancellationTokenSource();
        var attempts = 0;
        var logs = new List<string>();
        var delays = new List<TimeSpan>();
        var sampler = new RecordingSampler(new StrikeSampler(5));
        var publisher = new RecordingPublisher(attempt =>
        {
            attempts = attempt;
            if (attempt == 1)
            {
                throw new IOException("broker down");
            }

            cancellation.Cancel();
            return Task.CompletedTask;
        });
        var loop = new PublishLoop(
            sampler,
            publisher,
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            logs.Add);

        await loop.RunAsync(cancellation.Token);

        Assert.Equal(2, attempts);
        Assert.NotEmpty(logs);
        Assert.NotEmpty(sampler.Retries);
        Assert.InRange(sampler.Retries[0], 1, 30);
        Assert.Contains(TimeSpan.FromSeconds(sampler.Retries[0]), delays);
    }

    [Fact]
    public void Host_defaults_match_the_local_kafka_settings()
    {
        var settings = GeneratorSettings.From(_ => null);
        Assert.Equal("localhost:9092", settings.BootstrapServers);
        Assert.Equal("lightening-events", settings.Topic);
    }

    [Fact]
    public void Generator_publishes_and_does_not_reference_mongodb_or_a_consumer()
    {
        var root = RepoRoot();
        var project = Path.Combine(root, "src", "LightningGenerator");
        var source = string.Join('\n', Directory.GetFiles(project, "*.cs").Select(File.ReadAllText));
        var csproj = File.ReadAllText(Path.Combine(project, "LightningGenerator.csproj"));
        Assert.Contains("ProducerBuilder", source);
        Assert.DoesNotContain("ConsumerBuilder", source);
        Assert.DoesNotContain("IConsumer", source);
        Assert.DoesNotContain("MongoDB", csproj);
    }

    private static List<StrikeDraw> DrawStrikes(int seed, int count)
    {
        var sampler = new StrikeSampler(seed);
        return Enumerable.Range(0, count).Select(_ => sampler.NextStrike()).ToList();
    }

    private static List<int> Delays(int seed, int count)
    {
        var sampler = new StrikeSampler(seed);
        return Enumerable.Range(0, count).Select(_ => sampler.NextDelayMilliseconds()).ToList();
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

    private sealed class RecordingSampler : IStrikeSampler
    {
        private readonly IStrikeSampler _inner;

        public RecordingSampler(IStrikeSampler inner) => _inner = inner;

        public List<int> Retries { get; } = new();

        public StrikeDraw NextStrike() => _inner.NextStrike();

        public int NextDelayMilliseconds() => _inner.NextDelayMilliseconds();

        public int NextRetrySeconds()
        {
            var seconds = _inner.NextRetrySeconds();
            Retries.Add(seconds);
            return seconds;
        }
    }

    private sealed class RecordingPublisher : IStrikePublisher
    {
        private readonly Func<int, Task> _onAttempt;
        private int _attempts;

        public RecordingPublisher(Func<int, Task>? onAttempt = null)
        {
            _onAttempt = onAttempt ?? (_ => Task.CompletedTask);
        }

        public List<string> Payloads { get; } = new();

        public async Task PublishAsync(string json, CancellationToken cancellationToken)
        {
            _attempts++;
            Payloads.Add(json);
            await _onAttempt(_attempts);
        }
    }
}
