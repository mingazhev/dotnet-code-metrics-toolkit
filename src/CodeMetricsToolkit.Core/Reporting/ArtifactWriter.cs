using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Analysis;
using CodeMetricsToolkit.Core.Discovery;
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

    public static Task WriteAsync(
        string outputPath,
        SyntaxAnalysisFacts facts,
        string reportedRootPath,
        AnalyzeRequest request,
        DiscoveredSources sources,
        IReadOnlyList<MetricResultLine> metrics,
        IReadOnlyList<HotspotLine> hotspots,
        bool includeChunkText,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(reportedRootPath);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(hotspots);

        return ArtifactDirectoryPublisher.PublishAsync(
            outputPath,
            (stagingPath, token) => WriteArtifactsAsync(
                stagingPath,
                facts,
                reportedRootPath,
                request,
                sources,
                metrics,
                hotspots,
                includeChunkText,
                startedAt,
                completedAt,
                token),
            cancellationToken);
    }

    private static async Task WriteArtifactsAsync(
        string outputPath,
        SyntaxAnalysisFacts facts,
        string reportedRootPath,
        AnalyzeRequest request,
        DiscoveredSources sources,
        IReadOnlyList<MetricResultLine> metrics,
        IReadOnlyList<HotspotLine> hotspots,
        bool includeChunkText,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        await WriteJsonAsync(
            Path.Combine(outputPath, ArtifactNames.Summary),
            CreateSummary(facts, reportedRootPath, metrics, hotspots),
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

        // The manifest is the completion marker and is deliberately written last.
        await WriteJsonAsync(
            Path.Combine(outputPath, ArtifactNames.Manifest),
            CreateManifest(
                reportedRootPath,
                facts,
                request,
                sources,
                startedAt,
                completedAt),
            cancellationToken).ConfigureAwait(false);
    }

    private static ManifestArtifact CreateManifest(
        string rootPath,
        SyntaxAnalysisFacts facts,
        AnalyzeRequest request,
        DiscoveredSources sources,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        var durationMs = Math.Max(0, (long)(completedAt - startedAt).TotalMilliseconds);

        return new ManifestArtifact(
            ContractVersion.Current,
            ToolkitInfo.Name,
            ToolkitInfo.Version,
            rootPath,
            startedAt,
            completedAt,
            durationMs,
            facts.Mode,
            new InputSelectionArtifact(
                sources.SelectedSolutionPath is not null
                    ? "solution"
                    : sources.SelectedProjectPath is not null
                        ? "project"
                        : "directory",
                sources.SelectedSolutionPath ?? sources.SelectedProjectPath,
                SourcePopulationSha256(facts.Files),
                new AnalysisOptionsArtifact(
                    request.IncludePatterns.Order(StringComparer.Ordinal).ToArray(),
                    request.ExcludePatterns.Order(StringComparer.Ordinal).ToArray(),
                    request.IncludeGeneratedCode,
                    request.IncludeChunkText,
                    request.SyntaxOnly,
                    request.NoRestore,
                    request.IsolateInput,
                    request.Top)),
            new ArtifactMap(
                ArtifactNames.Summary,
                ArtifactNames.Metrics,
                ArtifactNames.Graph,
                ArtifactNames.Chunks,
                ArtifactNames.Diagnostics));
    }

    private static string SourcePopulationSha256(IReadOnlyList<FileFacts> files)
    {
        var population = string.Join(
            '\n',
            files.Select(file => file.FilePath).Order(StringComparer.Ordinal));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(population));

        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static SummaryArtifact CreateSummary(
        SyntaxAnalysisFacts facts,
        string reportedRootPath,
        IReadOnlyList<MetricResultLine> metrics,
        IReadOnlyList<HotspotLine> hotspots)
    {
        return new SummaryArtifact(
            ContractVersion.Current,
            reportedRootPath,
            facts.ProjectPaths.Count,
            facts.Files.Count,
            facts.Types.Count,
            facts.Members.Count,
            metrics.Count,
            facts.Diagnostics.Count,
            facts.Health,
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
        var json = JsonSerializer.Serialize(value, JsonOptions);

        await File.WriteAllTextAsync(path, json + "\n", cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WriteNdjsonAsync<T>(
        string path,
        IEnumerable<T> values,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = File.Create(path);
        await using var writer = new StreamWriter(stream);
        writer.NewLine = "\n";

        foreach (T value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var json = JsonSerializer.Serialize(value, NdjsonOptions);
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
        InputSelectionArtifact Input,
        ArtifactMap Artifacts);

    private sealed record InputSelectionArtifact(
        string Kind,
        string? SelectedPath,
        string SourcePopulationSha256,
        AnalysisOptionsArtifact Options);

    private sealed record AnalysisOptionsArtifact(
        IReadOnlyList<string> IncludePatterns,
        IReadOnlyList<string> ExcludePatterns,
        bool IncludeGeneratedCode,
        bool IncludeChunkText,
        bool SyntaxOnly,
        bool NoRestore,
        bool IsolateInput,
        int Top);

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
        AnalysisHealth AnalysisHealth,
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
