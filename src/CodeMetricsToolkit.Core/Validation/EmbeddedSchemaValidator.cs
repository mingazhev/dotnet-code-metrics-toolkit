using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace CodeMetricsToolkit.Core.Validation;

internal static class EmbeddedSchemaValidator
{
    private const string SchemaResourceSegment = ".Schemas.";

    private static readonly Lazy<Dictionary<string, JsonSchema>> Schemas =
        new(LoadSchemas, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<string> Validate(JsonElement instance, string schemaName)
    {
        if (!Schemas.Value.TryGetValue(schemaName, out JsonSchema? schema))
        {
            throw new InvalidOperationException($"embedded schema '{schemaName}' was not found.");
        }

        EvaluationResults results = schema.Evaluate(
            instance,
            new EvaluationOptions
            {
                OutputFormat = OutputFormat.List,
                RequireFormatValidation = true
            });

        if (results.IsValid)
        {
            return [];
        }

        var errors = EnumerateResults(results)
            .Where(result => !result.IsValid && result.Errors is { Count: > 0 })
            .SelectMany(result => result.Errors!.Select(error =>
            {
                var instanceLocation = result.InstanceLocation.ToString();
                var displayLocation = string.IsNullOrEmpty(instanceLocation) ? "/" : instanceLocation;

                return $"{displayLocation} [{error.Key}] {error.Value}";
            }))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return errors.Length == 0
            ? ["the schema did not report a specific keyword failure"]
            : errors;
    }

    private static Dictionary<string, JsonSchema> LoadSchemas()
    {
        Assembly assembly = typeof(EmbeddedSchemaValidator).Assembly;
        var resources = assembly
            .GetManifestResourceNames()
            .Where(name => name.Contains(SchemaResourceSegment, StringComparison.Ordinal) &&
                name.EndsWith(".schema.json", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();
        var schemas = new Dictionary<string, JsonSchema>(StringComparer.Ordinal);
        var buildOptions = new BuildOptions { SchemaRegistry = new SchemaRegistry() };

        foreach (var resourceName in resources)
        {
            var schemaNameIndex = resourceName.LastIndexOf(SchemaResourceSegment, StringComparison.Ordinal) +
                SchemaResourceSegment.Length;
            var schemaName = resourceName[schemaNameIndex..];

            using Stream stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"embedded schema '{schemaName}' could not be opened.");
            using var reader = new StreamReader(stream);

            if (!schemas.TryAdd(schemaName, JsonSchema.FromText(reader.ReadToEnd(), buildOptions)))
            {
                throw new InvalidOperationException($"embedded schema '{schemaName}' was found more than once.");
            }
        }

        if (schemas.Count == 0)
        {
            throw new InvalidOperationException("no embedded schemas were found.");
        }

        return schemas;
    }

    private static IEnumerable<EvaluationResults> EnumerateResults(EvaluationResults result)
    {
        yield return result;

        if (result.Details is null)
        {
            yield break;
        }

        foreach (EvaluationResults detail in result.Details)
        {
            foreach (EvaluationResults descendant in EnumerateResults(detail))
            {
                yield return descendant;
            }
        }
    }
}
