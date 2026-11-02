using MongoDB.Bson;
using MongoDB.Driver;

public sealed class MongoStrikeDocuments : IStrikeDocuments
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoStrikeDocuments(IMongoCollection<BsonDocument> collection)
    {
        _collection = collection;
    }

    public static bool IsCurrent(DateTime expiry, DateTime utcNow) => expiry > utcNow;

    public static FilterDefinition<BsonDocument> CurrentFilter(DateTime utcNow)
        => Builders<BsonDocument>.Filter.Gt("expiry", utcNow);

    public async Task<IReadOnlyList<StoredStrike>> GetUnexpiredAsync(DateTime utcNow, CancellationToken cancellationToken)
    {
        var documents = await _collection
            .Find(CurrentFilter(utcNow))
            .Sort(Builders<BsonDocument>.Sort.Descending("timestamp"))
            .ToListAsync(cancellationToken);
        return documents.Select(MapDocument).ToList();
    }

    public static StoredStrike MapDocument(BsonDocument document)
    {
        return new StoredStrike(
            document["ID"].AsString,
            document["latitude"].ToDouble(),
            document["longitude"].ToDouble(),
            document["intensity"].ToInt32(),
            document["timestamp"].ToUniversalTime(),
            document["expiry"].ToUniversalTime());
    }
}
