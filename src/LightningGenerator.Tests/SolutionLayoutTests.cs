using System.Text.RegularExpressions;

public class SolutionLayoutTests
{
    [Fact]
    public void Repository_contains_only_the_named_projects()
    {
        var root = RepoRoot();
        var solution = File.ReadAllText(Path.Combine(root, "Lightning.sln"));
        foreach (var project in new[] { "LightningGenerator", "KafkaServer", "LighteningListener", "MongoDBServer", "LightningWeb" })
        {
            Assert.Contains(project, solution);
        }

        var directories = Directory.GetDirectories(Path.Combine(root, "src"))
            .Select(Path.GetFileName)
            .OrderBy(name => name)
            .ToArray();
        Assert.Equal(
            new[]
            {
                "KafkaServer",
                "LighteningListener",
                "LighteningListener.Tests",
                "LightningGenerator",
                "LightningGenerator.Tests",
                "LightningWeb",
                "LightningWeb.Tests",
                "MongoDBServer"
            },
            directories);

        Assert.Empty(SourceFiles(Path.Combine(root, "src", "KafkaServer")));
        Assert.Empty(SourceFiles(Path.Combine(root, "src", "MongoDBServer")));
    }

    [Fact]
    public void Every_project_targets_net10()
    {
        var projects = Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "*.csproj", SearchOption.AllDirectories);
        Assert.NotEmpty(projects);
        foreach (var project in projects)
        {
            Assert.Contains("<TargetFramework>net10.0</TargetFramework>", File.ReadAllText(project));
        }
    }

    [Fact]
    public void Dockerfiles_use_the_required_final_images()
    {
        AssertFinalFrom("src/LightningGenerator/Dockerfile", "mcr.microsoft.com/dotnet/runtime:10.0");
        AssertFinalFrom("src/LighteningListener/Dockerfile", "mcr.microsoft.com/dotnet/runtime:10.0");
        AssertFinalFrom("src/LightningWeb/Dockerfile", "mcr.microsoft.com/dotnet/aspnet:10.0");

        var web = File.ReadAllText(Path.Combine(RepoRoot(), "src", "LightningWeb", "Dockerfile"));
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0", web);
        Assert.Contains("8080", web);

        var kafka = File.ReadAllText(Path.Combine(RepoRoot(), "src", "KafkaServer", "Dockerfile"));
        Assert.Contains("apache/kafka:4.4.0", kafka);
        Assert.Contains("kafka:9092", kafka);
        Assert.Contains("localhost:9092", kafka);

        var topicScript = File.ReadAllText(Path.Combine(RepoRoot(), "src", "KafkaServer", "start-kafka.sh"));
        Assert.Contains("--if-not-exists", topicScript);
        Assert.Contains("lightening-events", topicScript);
        Assert.Contains("--partitions 1", topicScript);
        Assert.Contains("--replication-factor 1", topicScript);

        var mongo = File.ReadAllText(Path.Combine(RepoRoot(), "src", "MongoDBServer", "Dockerfile"));
        Assert.Contains("mongo:8.0", mongo);
        var init = File.ReadAllText(Path.Combine(RepoRoot(), "src", "MongoDBServer", "init", "01-lightening.js"));
        Assert.Contains("Lightening", init);
        Assert.Contains("LighteningEvents", init);
        Assert.Contains("expiry_ttl", init);
        Assert.Contains("expireAfterSeconds: 0", init);
    }

    [Fact]
    public void Compose_wires_the_five_services_and_commits_no_map_key()
    {
        var root = RepoRoot();
        var compose = File.ReadAllText(Path.Combine(root, "docker-compose.yml"));
        foreach (var service in new[] { "kafka", "mongodb", "lightning-generator", "lightening-listener", "lightning-web" })
        {
            Assert.Contains($"{service}:", compose);
        }

        Assert.Contains("service_healthy", compose);
        Assert.Contains("unless-stopped", compose);
        Assert.Contains("mongo-data:", compose);
        Assert.Contains("8080:8080", compose);
        Assert.Contains("27017:27017", compose);
        Assert.Contains("9092:29092", compose);
        Assert.Contains("kafka:9092", compose);
        Assert.Contains("mongodb://mongodb:27017", compose);
        Assert.Contains("GOOGLE_MAPS_API_KEY: ${GOOGLE_MAPS_API_KEY:-}", compose);
        Assert.DoesNotContain("AIza", compose);

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            Assert.DoesNotMatch(new Regex(@"AIza[0-9A-Za-z_\-]{20,}"), File.ReadAllText(file));
        }
    }

    private static string[] SourceFiles(string directory)
    {
        var separator = Path.DirectorySeparatorChar;
        return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{separator}bin{separator}") && !path.Contains($"{separator}obj{separator}"))
            .ToArray();
    }

    private static void AssertFinalFrom(string relativePath, string image)
    {
        var docker = File.ReadAllText(Path.Combine(RepoRoot(), relativePath));
        Assert.Contains("mcr.microsoft.com/dotnet/sdk:10.0", docker);
        var froms = Regex.Matches(docker, @"^FROM\s+(\S+)", RegexOptions.Multiline);
        Assert.Equal(image, froms[^1].Groups[1].Value);
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
}
