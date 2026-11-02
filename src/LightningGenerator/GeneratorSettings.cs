public sealed record GeneratorSettings(string BootstrapServers, string Topic)
{
    public static GeneratorSettings FromEnvironment()
        => From(static name => Environment.GetEnvironmentVariable(name));

    public static GeneratorSettings From(Func<string, string?> read)
    {
        return new GeneratorSettings(
            Value(read, "KAFKA_BOOTSTRAP_SERVERS", "localhost:9092"),
            Value(read, "KAFKA_TOPIC", "lightening-events"));
    }

    private static string Value(Func<string, string?> read, string name, string fallback)
    {
        var value = read(name);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
