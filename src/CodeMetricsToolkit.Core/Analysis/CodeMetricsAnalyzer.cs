using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Core.Analysis;

public static class CodeMetricsAnalyzer
{
    private static readonly StringComparison PathComparison = OperatingSystem.IsWindows()
        ? StringComparison.OrdinalIgnoreCase
        : StringComparison.Ordinal;

    public static async Task<AnalysisRunResult> AnalyzeAsync(
        AnalyzeRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        InputSelection originalInput = SourceFileDiscovery.ResolveInputSelection(request.InputPath);
        var outputPath = NormalizePath(request.OutputPath);
        ValidateOutputLocation(outputPath, originalInput.RootPath);

        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        IsolatedInput? isolatedInput = request.IsolateInput
            ? InputIsolator.CopyToTemporaryDirectory(request.InputPath, cancellationToken)
            : null;
        var inputPath = isolatedInput?.IsolatedInputPath ?? request.InputPath;
        var warnings = new List<string>();

        try
        {
            DiscoveredSources sources = SourceFileDiscovery.Discover(
                inputPath,
                request.IncludeGeneratedCode,
                request.IncludePatterns,
                request.ExcludePatterns,
                cancellationToken);
            var reportedRootPath = isolatedInput?.OriginalRootPath ?? sources.RootPath;
            MSBuildRegistration.RegistrationResult registration = request.SyntaxOnly
                ? MSBuildRegistration.RegistrationResult.Available
                : MSBuildRegistration.EnsureRegistered(sources.RootPath);
            SyntaxAnalysisFacts facts = await SyntaxFactsCollector.CollectAsync(
                    sources,
                    useSemantic: !request.SyntaxOnly && registration.IsAvailable,
                    noRestore: request.NoRestore,
                    cancellationToken,
                    registration.FailureMessage)
                .ConfigureAwait(false);

            if (isolatedInput is not null)
            {
                facts = facts with
                {
                    Health = facts.Health with
                    {
                        Messages = facts.Health.Messages
                            .Append("Input was analyzed from an isolated temporary copy.")
                            .ToArray()
                    }
                };
            }

            HotspotRanking hotspotRanking = HotspotRanker.Rank(facts, request.Top, cancellationToken);
            MetricResultLine[] metrics = SyntaxMetricProjector.Project(facts, cancellationToken)
                .Concat(GraphMetricProjector.Project(facts, cancellationToken))
                .Concat(SemanticMetricProjector.Project(facts, cancellationToken))
                .Concat(DiagnosticMetricProjector.Project(facts, cancellationToken))
                .Concat(hotspotRanking.Metrics)
                .ToArray();
            DateTimeOffset completedAt = DateTimeOffset.UtcNow;

            IReadOnlyList<string> publicationWarnings = await ArtifactWriter.WriteAsync(
                outputPath,
                facts,
                reportedRootPath,
                request,
                sources,
                metrics,
                hotspotRanking.Hotspots,
                request.IncludeChunkText,
                startedAt,
                completedAt,
                cancellationToken).ConfigureAwait(false);
            warnings.AddRange(publicationWarnings);

            if (isolatedInput is not null)
            {
                if (!isolatedInput.TryDispose(out var cleanupFailure))
                {
                    warnings.Add(cleanupFailure!);
                }

                isolatedInput = null;
            }

            return new AnalysisRunResult
            {
                OutputPath = outputPath,
                Warnings = warnings,
                Summary = new AnalysisSummary
                {
                    RootPath = reportedRootPath,
                    ProjectCount = facts.ProjectPaths.Count,
                    FileCount = facts.Files.Select(file => file.FilePath).Distinct(StringComparer.Ordinal).Count(),
                    TypeCount = facts.Types.Count,
                    MemberCount = facts.Members.Count,
                    MetricResultCount = metrics.Length,
                    DiagnosticCount = facts.Diagnostics.Count,
                    Health = facts.Health
                }
            };
        }
        catch (Exception exception)
        {
            if (isolatedInput is not null && !isolatedInput.TryDispose(out var cleanupFailure))
            {
                isolatedInput = null;
                if (exception is OperationCanceledException canceled)
                {
                    throw new OperationCanceledException(
                        $"{canceled.Message} Isolated input cleanup also failed. {cleanupFailure}",
                        canceled,
                        canceled.CancellationToken);
                }

                throw new AggregateException(
                    $"Analysis failed, and isolated input cleanup also failed: {cleanupFailure}",
                    exception,
                    new IOException(cleanupFailure));
            }

            throw;
        }
        finally
        {
            if (isolatedInput is not null)
            {
                _ = isolatedInput.TryDispose(out _);
            }
        }
    }

    private static void ValidateOutputLocation(string outputPath, string originalInputRoot)
    {
        outputPath = NormalizePath(outputPath);
        var inputRoot = NormalizePath(originalInputRoot);

        if (IsSamePath(outputPath, inputRoot) || IsAncestorOf(outputPath, inputRoot))
        {
            throw new ArgumentException(
                $"Artifact output directory cannot be the analyzed input root or one of its ancestors: " +
                outputPath,
                nameof(outputPath));
        }
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(left, right, PathComparison);
    }

    private static bool IsAncestorOf(string possibleAncestor, string path)
    {
        var ancestorPrefix = Path.EndsInDirectorySeparator(possibleAncestor)
            ? possibleAncestor
            : possibleAncestor + Path.DirectorySeparatorChar;

        return path.StartsWith(ancestorPrefix, PathComparison);
    }

    private static string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = ResolveReparseComponents(Path.GetFullPath(path));

        if (OperatingSystem.IsMacOS())
        {
            fullPath = ResolveMacOsRootAlias(fullPath);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(fullPath));
    }

    private static string ResolveReparseComponents(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
        {
            return fullPath;
        }

        var current = Path.TrimEndingDirectorySeparator(root);
        if (current.Length == 0)
        {
            current = root;
        }

        var relative = fullPath[root.Length..];
        foreach (var segment in relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            current = ResolveIfReparse(Path.Combine(current, segment));
        }

        return current;
    }

    private static string ResolveIfReparse(string path)
    {
        try
        {
            if (!Path.Exists(path))
            {
                return path;
            }

            FileAttributes attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                return path;
            }

            FileSystemInfo info = (attributes & FileAttributes.Directory) != 0
                ? new DirectoryInfo(path)
                : new FileInfo(path);
            FileSystemInfo? target = info.ResolveLinkTarget(returnFinalTarget: true);
            return target?.FullName ?? path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return path;
        }
    }

    private static string ResolveMacOsRootAlias(string fullPath)
    {
        var root = Path.GetPathRoot(fullPath)!;
        var relativePath = Path.GetRelativePath(root, fullPath);
        var separator = relativePath.IndexOfAny(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        var firstComponent = separator < 0 ? relativePath : relativePath[..separator];
        var firstPath = Path.Combine(root, firstComponent);

        if (!Directory.Exists(firstPath) ||
            (File.GetAttributes(firstPath) & FileAttributes.ReparsePoint) == 0)
        {
            return fullPath;
        }

        FileSystemInfo? target = new DirectoryInfo(firstPath).ResolveLinkTarget(returnFinalTarget: true);
        if (target is null || separator < 0)
        {
            return target?.FullName ?? fullPath;
        }

        return Path.Combine(target.FullName, relativePath[(separator + 1)..]);
    }
}
