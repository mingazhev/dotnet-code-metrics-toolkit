using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace CodeMetricsToolkit.Tests.Scoring;

internal static class ScoringTestSupport
{
    private static readonly JsonSerializerOptions GraphJsonOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    internal static MemoryStream ProfileStream(string json)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(json));
    }

    internal static string ValidProfile(int minTargetCount = 1)
    {
        return Profile(ThresholdOperation() + "," + WeightedOperation(), minTargetCount);
    }

    internal static string Profile(
        string operations,
        int minTargetCount = 1,
        string primaryKey = "score")
    {
        return $$"""
            {
              "schemaVersion": "1.0.0",
              "id": "maintainability-baseline",
              "version": "1.0.0",
              "artifactContractVersion": "0.1.0",
              "allowedAnalysisModes": ["semantic"],
              "allowedTargetIdStabilities": ["semantic"],
              "selectors": {
                "includeFilePaths": ["src/**/*.cs"],
                "excludeFilePaths": ["src/Generated/**"],
                "requireTrusted": true,
                "minTargetCount": {{minTargetCount}}
              },
              "primary": {
                "key": "{{primaryKey}}",
                "direction": "minimize"
              },
              "operations": [
                {{operations}}
              ]
            }
            """;
    }

    internal static string ThresholdOperation()
    {
        return """
            {
              "operation": "thresholdDebt",
              "targetKind": "member",
              "thresholds": [
                {
                  "metricId": "cyclomatic_complexity",
                  "metricVersion": "1.0.0",
                  "maximum": 10,
                  "weight": 2
                },
                {
                  "metricId": "member_length",
                  "metricVersion": "1.0.0",
                  "maximum": 50
                }
              ],
              "outputs": {
                "gapSum": "debt",
                "squaredGapSum": "squared_debt",
                "violatingTargetCount": "violating_targets",
                "maxTargetGap": "max_target_gap"
              }
            }
            """;
    }

    internal static string WeightedOperation()
    {
        return """
            {
              "operation": "weightedSum",
              "terms": [
                { "key": "debt", "weight": 1 },
                { "key": "squared_debt", "weight": 0.1 }
              ],
              "output": "score"
            }
            """;
    }

    internal static string DiagnosticThresholdOperation(string? tag = null)
    {
        var tags = tag is null ? string.Empty : $", \"tags\": [\"{tag}\"]";

        return $$"""
            {
              "operation": "thresholdDebt",
              "targetKind": "member",
              "thresholds": [
                {
                  "metricId": "diagnostic_count",
                  "metricVersion": "1.0.0",
                  "maximum": 2{{tags}}
                }
              ],
              "outputs": {
                "gapSum": "debt",
                "squaredGapSum": "squared_debt",
                "violatingTargetCount": "violating_targets",
                "maxTargetGap": "max_target_gap"
              }
            }
            """;
    }

    internal static string FileThresholdOperation()
    {
        return """
            {
              "operation": "thresholdDebt",
              "targetKind": "file",
              "thresholds": [
                {
                  "metricId": "lines_of_code",
                  "metricVersion": "1.0.0",
                  "maximum": 100
                }
              ],
              "outputs": {
                "gapSum": "file_debt",
                "squaredGapSum": "file_squared",
                "violatingTargetCount": "file_violations",
                "maxTargetGap": "file_max"
              }
            }
            """;
    }

    internal static string TypeThresholdOperation()
    {
        return """
            {
              "operation": "thresholdDebt",
              "targetKind": "type",
              "thresholds": [
                {
                  "metricId": "lines_of_code",
                  "metricVersion": "1.0.0",
                  "maximum": 50
                }
              ],
              "outputs": {
                "gapSum": "debt",
                "squaredGapSum": "squared_debt",
                "violatingTargetCount": "violating_targets",
                "maxTargetGap": "max_target_gap"
              }
            }
            """;
    }

    internal static string Metric(
        string targetId,
        string filePath,
        string metricId,
        string metricVersion,
        double value,
        params string[] tags)
    {
        return MetricForTarget(
            "member",
            targetId,
            filePath,
            metricId,
            metricVersion,
            value,
            tags: tags);
    }

    internal static string MetricForTarget(
        string targetKind,
        string targetId,
        string filePath,
        string metricId,
        string metricVersion,
        double value,
        string stability = "semantic",
        params string[] tags)
    {
        var tagsJson = tags.Length == 0
            ? string.Empty
            : $",\"tags\":{JsonSerializer.Serialize(tags)}";

        return $$"""
            {"schemaVersion":"0.1.0","metricId":"{{metricId}}","metricVersion":"{{metricVersion}}","targetId":"{{targetId}}","targetKind":"{{targetKind}}","targetIdStability":"{{stability}}","valueKind":"integer","numericValue":{{value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}},"filePath":"{{filePath}}"{{tagsJson}}}
            """;
    }

    internal sealed class TestArtifacts : IDisposable
    {
        private TestArtifacts(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TestArtifacts Create(string analysisQuality, params string[] metrics)
        {
            return CreateWithOptions(
                analysisQuality,
                trustedDiagnostics: true,
                artifactSchemaVersion: "0.1.0",
                metrics);
        }

        public static TestArtifacts CreateWithOptions(
            string analysisQuality,
            bool trustedDiagnostics,
            string artifactSchemaVersion,
            params string[] metrics)
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"codemetrics-scoring-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            File.WriteAllText(
                System.IO.Path.Combine(path, "manifest.json"),
                $$"""{"schemaVersion":"{{artifactSchemaVersion}}","mode":"semantic"}""");
            File.WriteAllText(
                System.IO.Path.Combine(path, "summary.json"),
                $$"""
                {
                  "schemaVersion": "{{artifactSchemaVersion}}",
                  "rootPath": "/repo",
                  "metricResultCount": {{metrics.Length}},
                  "analysisHealth": {
                    "analysisQuality": "{{analysisQuality}}",
                    "trustedDiagnostics": {{trustedDiagnostics.ToString().ToLowerInvariant()}}
                  }
                }
                """);
            File.WriteAllLines(System.IO.Path.Combine(path, "metrics.ndjson"), metrics);

            var targets = new Dictionary<string, GraphFixtureTarget>(StringComparer.Ordinal);
            foreach (var metric in metrics)
            {
                using var document = JsonDocument.Parse(metric);
                JsonElement root = document.RootElement;
                var targetId = root.GetProperty("targetId").GetString()!;
                var targetKind = root.GetProperty("targetKind").GetString()!;
                var filePath = root.GetProperty("filePath").GetString()!;
                var targetIdStability = root.GetProperty("targetIdStability").GetString()!;
                targets.TryAdd(
                    targetId,
                    new GraphFixtureTarget(
                        targetId,
                        targetKind,
                        filePath,
                        [filePath],
                        TargetIdStability: targetIdStability));
            }

            WriteGraphFile(path, artifactSchemaVersion, targets.Values);

            return new TestArtifacts(path);
        }

        public void WriteGraph(
            string artifactSchemaVersion,
            params GraphFixtureTarget[] targets)
        {
            WriteGraphFile(Path, artifactSchemaVersion, targets);
        }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }

        internal static void WriteGraphFile(
            string artifactPath,
            string artifactSchemaVersion,
            IEnumerable<GraphFixtureTarget> targets)
        {
            var nodes = targets
                .OrderBy(target => target.TargetId, StringComparer.Ordinal)
                .Select(target => new
                {
                    id = target.TargetId,
                    kind = target.TargetKind,
                    name = target.TargetId,
                    targetIdStability = target.TargetIdStability,
                    filePath = target.PrimaryFilePath,
                    startLine = 1,
                    endLine = 1,
                    declarations = target.HasCompleteDeclarationPaths &&
                        target.TargetKind is "type" or "member"
                        ? target.FilePaths
                            .Select(filePath => new { filePath, startLine = 1, endLine = 1 })
                            .ToArray()
                        : null
                })
                .Cast<object>()
                .ToArray();
            var graphJson = JsonSerializer.Serialize(
                new
                {
                    schemaVersion = artifactSchemaVersion,
                    nodes,
                    edges = Array.Empty<object>()
                },
                GraphJsonOptions);
            File.WriteAllText(System.IO.Path.Combine(artifactPath, "graph.json"), graphJson);
        }
    }

    internal sealed record GraphFixtureTarget(
        string TargetId,
        string TargetKind,
        string PrimaryFilePath,
        IReadOnlyList<string> FilePaths,
        bool HasCompleteDeclarationPaths = true,
        string TargetIdStability = "semantic");
}
