namespace CodeMetricsToolkit.Tests.SchemaValidation;

public sealed class SchemaValidationTests
{
    [Theory]
    [InlineData("manifest.schema.json", "manifest.json")]
    [InlineData("summary.schema.json", "summary.json")]
    [InlineData("graph.schema.json", "graph.json")]
    public void JsonArtifactValidatesAgainstSchema(string schemaFileName, string artifactFileName)
    {
        SchemaAssertions.JsonFileValidates(schemaFileName, FixturePath("ValidMinimal", artifactFileName));
    }

    [Theory]
    [InlineData("metric-result.schema.json", "metrics.ndjson")]
    [InlineData("chunk.schema.json", "chunks.ndjson")]
    [InlineData("diagnostic.schema.json", "diagnostics.ndjson")]
    public void NdjsonArtifactLinesValidateAgainstSchema(string schemaFileName, string artifactFileName)
    {
        SchemaAssertions.NdjsonFileValidates(schemaFileName, FixturePath("ValidMinimal", artifactFileName));
    }

    [Fact]
    public void MetricResultSchemaRejectsLineWithoutExactlyOneValue()
    {
        SchemaAssertions.NdjsonFileDoesNotValidate(
            "metric-result.schema.json",
            FixturePath("InvalidMetricMissingValue", "metrics.ndjson"));
    }

    [Fact]
    public void ManifestSchemaRejectsMissingMandatoryArtifact()
    {
        SchemaAssertions.JsonFileDoesNotValidate(
            "manifest.schema.json",
            FixturePath("InvalidManifestMissingArtifact", "manifest.json"));
    }

    private static string FixturePath(string fixtureName, string artifactFileName)
    {
        return Path.Combine(
            SchemaAssertions.RepositoryRoot(),
            "tests",
            "CodeMetricsToolkit.Tests",
            "Fixtures",
            "ExpectedOutput",
            fixtureName,
            artifactFileName);
    }
}
