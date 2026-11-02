using MongoDB.Bson;
using MongoDB.Driver;

public sealed class MongoStrikeStore : IStrikeStore
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoStrikeStore(IMongoCollection<BsonDocument> collection)
    {
        _collection = collection;
    }

    public async Task InsertAsync(StrikeDocument document, CancellationToken cancellationToken)
    {
        var bson = new BsonDocument
        {
            { "_id", document.Id },
            { "ID", document.ID },
            { "latitude", document.Latitude },
            { "longitude", document.Longitude },
            { "intensity", document.Intensity },
            { "timestamp", document.Timestamp },
            { "expiry", document.Expiry }
        };
        await _collection.InsertOneAsync(bson, cancellationToken: cancellationToken);
    }
}
