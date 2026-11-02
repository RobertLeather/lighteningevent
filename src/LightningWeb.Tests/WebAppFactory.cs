using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

public sealed class MemoryStrikes : IStrikeDocuments
{
    public List<StoredStrike> Docs { get; } = new();

    public bool Fail { get; set; }

    public Task<IReadOnlyList<StoredStrike>> GetUnexpiredAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        if (Fail)
        {
            throw new InvalidOperationException("mongodb down");
        }

        IReadOnlyList<StoredStrike> current = Docs
            .Where(document => MongoStrikeDocuments.IsCurrent(document.Expiry, utcNow))
            .OrderByDescending(document => document.Timestamp)
            .ToList();
        return Task.FromResult(current);
    }
}

public sealed class FixedClock : TimeProvider
{
    private readonly DateTimeOffset _utcNow;

    public FixedClock(DateTimeOffset utcNow) => _utcNow = utcNow;

    public override DateTimeOffset GetUtcNow() => _utcNow;
}

public sealed class LightningWebFactory : WebApplicationFactory<Program>
{
    public MemoryStrikes Strikes { get; } = new();

    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 11, 2, 12, 0, 0, TimeSpan.Zero));

    public string MapsApiKey { get; set; } = "";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.AddSingleton<IStrikeDocuments>(Strikes);
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton(new MapOptions { ApiKey = MapsApiKey });
        });
    }
}
