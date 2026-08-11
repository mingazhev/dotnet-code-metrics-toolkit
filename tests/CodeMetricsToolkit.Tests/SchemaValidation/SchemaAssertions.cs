using System.Text.Json;
using Json.Schema;

namespace CodeMetricsToolkit.Tests.SchemaValidation;

internal static class SchemaAssertions
{
    private static readonly Lazy<Dictionary<string, JsonSchema>> Schemas =
        new(LoadSchemas, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void JsonFileValidates(string schemaFileName, string artifactPath)
    {
        JsonSchema schema = LoadSchema(schemaFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(artifactPath));

        EvaluationResults results = schema.Evaluate(document.RootElement, CreateValidationOptions());

        Assert.True(results.IsValid, FormatErrors(results));
    }

    public static void JsonFileDoesNotValidate(string schemaFileName, string artifactPath)
    {
        JsonSchema schema = LoadSchema(schemaFileName);
        using var document = JsonDocument.Parse(File.ReadAllText(artifactPath));

        EvaluationResults results = schema.Evaluate(document.RootElement, CreateValidationOptions());

        Assert.False(results.IsValid);
    }

    public static void NdjsonFileValidates(string schemaFileName, string artifactPath)
    {
        JsonSchema schema = LoadSchema(schemaFileName);

        foreach (JsonElement line in LoadNdjsonElements(artifactPath))
        {
            EvaluationResults results = schema.Evaluate(line, CreateValidationOptions());

            Assert.True(results.IsValid, FormatErrors(results));
        }
    }

    public static void NdjsonFileDoesNotValidate(string schemaFileName, string artifactPath)
    {
        JsonSchema schema = LoadSchema(schemaFileName);
        JsonElement instance = LoadNdjsonElements(artifactPath).Single();

        EvaluationResults results = schema.Evaluate(instance, CreateValidationOptions());

        Assert.False(results.IsValid);
    }

    public static void OutputDirectoryValidates(string outputPath)
    {
        JsonFileValidates("manifest.schema.json", Path.Combine(outputPath, "manifest.json"));
        JsonFileValidates("summary.schema.json", Path.Combine(outputPath, "summary.json"));
        JsonFileValidates("graph.schema.json", Path.Combine(outputPath, "graph.json"));
        NdjsonFileValidates("metric-result.schema.json", Path.Combine(outputPath, "metrics.ndjson"));
        NdjsonFileValidates("chunk.schema.json", Path.Combine(outputPath, "chunks.ndjson"));
        NdjsonFileValidates("diagnostic.schema.json", Path.Combine(outputPath, "diagnostics.ndjson"));
    }

    public static string RepositoryRoot()
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

    private static JsonSchema LoadSchema(string fileName)
    {
        return Schemas.Value.TryGetValue(fileName, out JsonSchema? schema)
            ? schema
            : throw new InvalidOperationException($"Schema '{fileName}' was not found.");
    }

    private static Dictionary<string, JsonSchema> LoadSchemas()
    {
        var schemaDirectory = Path.Combine(RepositoryRoot(), "schemas");
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        var buildOptions = new BuildOptions { SchemaRegistry = new SchemaRegistry() };

        foreach (var path in Directory.EnumerateFiles(schemaDirectory, "*.schema.json")
                     .Order(StringComparer.Ordinal))
        {
            var fileName = Path.GetFileName(path);

            schemas.Add(fileName, JsonSchema.FromText(File.ReadAllText(path), buildOptions));
        }

        return schemas;
    }

    private static EvaluationOptions CreateValidationOptions()
    {
        return new EvaluationOptions { OutputFormat = OutputFormat.List };
    }

    private static List<JsonElement> LoadNdjsonElements(string artifactPath)
    {
        var elements = new List<JsonElement>();

        foreach (var line in File.ReadLines(artifactPath))
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

    private static string FormatErrors(EvaluationResults results)
    {
        Dictionary<string, string>? errors = results.Errors;

        if (errors is null || errors.Count == 0)
        {
            return "Schema validation failed without reported errors.";
        }

        return string.Join(
            Environment.NewLine,
            errors.Select(error => $"{error.Key}: {error.Value}"));
    }
}
