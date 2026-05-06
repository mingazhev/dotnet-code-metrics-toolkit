using System.Text.Json;
using System.Text.Json.Serialization;
using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Core.Reporting;

public static class ArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static readonly JsonSerializerOptions NdjsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static async Task WriteAsync(
        string outputPath,
        SyntaxAnalysisFacts facts,
        IReadOnlyList<MetricResultLine> metrics,
        IReadOnlyList<HotspotLine> hotspots,
        bool includeChunkText,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(hotspots);

        Directory.CreateDirectory(outputPath);

        await WriteJsonAsync(
            Path.Combine(outputPath, ArtifactNames.Manifest),
            CreateManifest(facts.RootPath, facts.Mode, startedAt, completedAt),
            cancellationToken).ConfigureAwait(false);

        await WriteJsonAsync(
            Path.Combine(outputPath, ArtifactNames.Summary),
            CreateSummary(facts, metrics, hotspots),
            cancellationToken).ConfigureAwait(false);

        await WriteNdjsonAsync(
            Path.Combine(outputPath, ArtifactNames.Metrics),
            metrics,
            cancellationToken).ConfigureAwait(false);

        await WriteJsonAsync(
            Path.Combine(outputPath, ArtifactNames.Graph),
            GraphProjector.Project(facts),
            cancellationToken).ConfigureAwait(false);

        await WriteNdjsonAsync(
            Path.Combine(outputPath, ArtifactNames.Chunks),
            ChunkProjector.Project(facts, includeChunkText),
            cancellationToken).ConfigureAwait(false);

        await WriteNdjsonAsync(
            Path.Combine(outputPath, ArtifactNames.Diagnostics),
            facts.Diagnostics.Select(ToDiagnosticLine),
            cancellationToken).ConfigureAwait(false);
    }

    private static ManifestArtifact CreateManifest(
        string rootPath,
        string mode,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        long durationMs = Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);

        return new ManifestArtifact(
            ContractVersion.Current,
            "CodeMetricsToolkit",
            "0.1.0",
            rootPath,
            startedAt,
            completedAt,
            durationMs,
            mode,
            new ArtifactMap(
                ArtifactNames.Summary,
                ArtifactNames.Metrics,
                ArtifactNames.Graph,
                ArtifactNames.Chunks,
                ArtifactNames.Diagnostics));
    }

    private static SummaryArtifact CreateSummary(
        SyntaxAnalysisFacts facts,
        IReadOnlyList<MetricResultLine> metrics,
        IReadOnlyList<HotspotLine> hotspots)
    {
        return new SummaryArtifact(
            ContractVersion.Current,
            facts.RootPath,
            facts.ProjectPaths.Count,
            facts.Files.Count,
            facts.Types.Count,
            facts.Members.Count,
            metrics.Count,
            facts.Diagnostics.Count,
            hotspots);
    }

    private static DiagnosticLine ToDiagnosticLine(AnalysisDiagnostic diagnostic)
    {
        return new DiagnosticLine(
            ContractVersion.Current,
            diagnostic.Id,
            diagnostic.Severity,
            diagnostic.Message,
            diagnostic.ProjectPath,
            diagnostic.FilePath,
            diagnostic.StartLine,
            diagnostic.EndLine,
            diagnostic.Tags);
    }

    private static async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(value, JsonOptions);

        await File.WriteAllTextAsync(path, json + Environment.NewLine, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteNdjsonAsync<T>(
        string path,
        IEnumerable<T> values,
        CancellationToken cancellationToken)
    {
        await using var stream = File.Create(path);
        await using var writer = new StreamWriter(stream);

        foreach (T value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string json = JsonSerializer.Serialize(value, NdjsonOptions);
            await writer.WriteLineAsync(json).ConfigureAwait(false);
        }
    }

    private sealed record ManifestArtifact(
        string SchemaVersion,
        string Tool,
        string ToolVersion,
        string RootPath,
        DateTimeOffset StartedAt,
        DateTimeOffset CompletedAt,
        long DurationMs,
        string Mode,
        ArtifactMap Artifacts);

    private sealed record ArtifactMap(
        string Summary,
        string Metrics,
        string Graph,
        string Chunks,
        string Diagnostics);

    private sealed record SummaryArtifact(
        string SchemaVersion,
        string RootPath,
        int ProjectCount,
        int FileCount,
        int TypeCount,
        int MemberCount,
        int MetricResultCount,
        int DiagnosticCount,
        IReadOnlyList<HotspotLine> Hotspots);

    private sealed record DiagnosticLine(
        string SchemaVersion,
        string Id,
        string Severity,
        string Message,
        string? ProjectPath,
        string? FilePath,
        int? StartLine,
        int? EndLine,
        IReadOnlyList<string>? Tags);
}
