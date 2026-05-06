using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;

namespace CodeMetricsToolkit.Tests.SchemaValidation;

public sealed class SchemaValidationTests
{
    private static readonly ConcurrentDictionary<string, JsonSchema> SchemaCache = new(StringComparer.Ordinal);

    private static readonly EvaluationOptions ValidationOptions = new()
    {
        OutputFormat = OutputFormat.List
    };

    [Theory]
    [InlineData("manifest.schema.json", "manifest.json")]
    [InlineData("summary.schema.json", "summary.json")]
    [InlineData("graph.schema.json", "graph.json")]
    public void JsonArtifactValidatesAgainstSchema(string schemaFileName, string artifactFileName)
    {
        JsonSchema schema = LoadSchema(schemaFileName);
        JsonElement instance = LoadJsonElement("ValidMinimal", artifactFileName);

        EvaluationResults results = schema.Evaluate(instance, ValidationOptions);

        Assert.True(results.IsValid, FormatErrors(results));
    }

    [Theory]
    [InlineData("metric-result.schema.json", "metrics.ndjson")]
    [InlineData("chunk.schema.json", "chunks.ndjson")]
    [InlineData("diagnostic.schema.json", "diagnostics.ndjson")]
    public void NdjsonArtifactLinesValidateAgainstSchema(string schemaFileName, string artifactFileName)
    {
        JsonSchema schema = LoadSchema(schemaFileName);

        foreach (JsonElement line in LoadNdjsonElements("ValidMinimal", artifactFileName))
        {
            EvaluationResults results = schema.Evaluate(line, ValidationOptions);

            Assert.True(results.IsValid, FormatErrors(results));
        }
    }

    [Fact]
    public void MetricResultSchemaRejectsLineWithoutExactlyOneValue()
    {
        JsonSchema schema = LoadSchema("metric-result.schema.json");
        JsonElement instance = LoadNdjsonElements("InvalidMetricMissingValue", "metrics.ndjson").Single();

        EvaluationResults results = schema.Evaluate(instance, ValidationOptions);

        Assert.False(results.IsValid);
    }

    [Fact]
    public void ManifestSchemaRejectsMissingMandatoryArtifact()
    {
        JsonSchema schema = LoadSchema("manifest.schema.json");
        JsonElement instance = LoadJsonElement("InvalidManifestMissingArtifact", "manifest.json");

        EvaluationResults results = schema.Evaluate(instance, ValidationOptions);

        Assert.False(results.IsValid);
    }

    private static JsonSchema LoadSchema(string fileName)
    {
        return SchemaCache.GetOrAdd(fileName, static cachedFileName =>
        {
            string path = Path.Combine(FindRepositoryRoot(), "schemas", cachedFileName);

            return JsonSchema.FromText(File.ReadAllText(path));
        });
    }

    private static JsonElement LoadJsonElement(string fixtureName, string artifactFileName)
    {
        string path = FixturePath(fixtureName, artifactFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(path));

        return document.RootElement.Clone();
    }

    private static List<JsonElement> LoadNdjsonElements(string fixtureName, string artifactFileName)
    {
        string path = FixturePath(fixtureName, artifactFileName);
        var elements = new List<JsonElement>();

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            elements.Add(document.RootElement.Clone());
        }

        return elements;
    }

    private static string FixturePath(string fixtureName, string artifactFileName)
    {
        return Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "CodeMetricsToolkit.Tests",
            "Fixtures",
            "ExpectedOutput",
            fixtureName,
            artifactFileName);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "schemas")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }

    private static string FormatErrors(EvaluationResults results)
    {
        IReadOnlyDictionary<string, string>? errors = results.Errors;

        if (errors is null || errors.Count == 0)
        {
            return "Schema validation failed without reported errors.";
        }

        return string.Join(
            Environment.NewLine,
            errors.Select(error => $"{error.Key}: {error.Value}"));
    }
}
