public sealed record ListenerSettings(
    string BootstrapServers,
    string Topic,
    string GroupId,
    string MongoConnectionString,
    string Database,
    string Collection)
{
    public static ListenerSettings HostDefaults { get; } = new(
        "localhost:9092",
        "lightening-events",
        "lightening-listener",
        "mongodb://localhost:27017",
        "Lightening",
        "LighteningEvents");

    public static ListenerSettings FromEnvironment()
        => From(static name => Environment.GetEnvironmentVariable(name));

    public static ListenerSettings From(Func<string, string?> read)
    {
        return HostDefaults with
        {
            BootstrapServers = Value(read, "KAFKA_BOOTSTRAP_SERVERS", HostDefaults.BootstrapServers),
            Topic = Value(read, "KAFKA_TOPIC", HostDefaults.Topic),
            GroupId = Value(read, "KAFKA_GROUP_ID", HostDefaults.GroupId),
            MongoConnectionString = Value(read, "MONGODB_CONNECTION_STRING", HostDefaults.MongoConnectionString),
            Database = Value(read, "MONGODB_DATABASE", HostDefaults.Database),
            Collection = Value(read, "MONGODB_COLLECTION", HostDefaults.Collection)
        };
    }

    private static string Value(Func<string, string?> read, string name, string fallback)
    {
        var value = read(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
