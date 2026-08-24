using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.MSBuild;

namespace CodeMetricsToolkit.Core.Syntax;

internal static class SemanticWorkspaceLoader
{
    public static async Task<SemanticLoadResult> LoadAsync(
        DiscoveredSources sources,
        bool noRestore,
        List<AnalysisDiagnostic> diagnostics,
        HashSet<string> diagnosticKeys,
        CancellationToken cancellationToken)
    {
        var messages = DetectAmbientBuildFiles(sources.RootPath).ToList();
        DotnetRestoreRunner.RestoreResult restore = await DotnetRestoreRunner
            .RestoreAsync(sources, noRestore, cancellationToken)
            .ConfigureAwait(false);
        messages.AddRange(restore.Messages);
        if (restore.Status == "failed")
        {
            messages.Add("Compiler diagnostics were suppressed because restore failed; emitted diagnostics are not treated as code-quality metrics.");
        }

        if (sources.ProjectPaths.Count == 0 ||
            sources.ProjectPaths.All(ProjectIdentity.IsSynthetic))
        {
            messages.Add("No C# project files were discovered; semantic analysis fell back to syntax-only parsing.");
            return Unavailable(restore.Status, messages);
        }

        try
        {
            using var workspace = MSBuildWorkspace.Create(CreateWorkspaceProperties());
            workspace.SkipUnrecognizedProjects = true;
            workspace.LoadMetadataForReferencedProjects = false;

            var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
            workspace.RegisterWorkspaceFailedHandler(args => workspaceDiagnostics.Add(args.Diagnostic));

            Solution solution = await LoadSolutionAsync(sources, workspace, cancellationToken).ConfigureAwait(false);
            IReadOnlySet<string> includedSourcePaths = sources.CandidateSourceFiles
                .Select(source => NormalizeFullPath(source.FullPath))
                .ToHashSet(StringComparer.Ordinal);
            Dictionary<string, ProjectSemanticContext> contexts = await CreateProjectContextsAsync(
                    sources.RootPath,
                    solution,
                    includedSourcePaths,
                    collectCompilationDiagnostics: restore.Status != "failed",
                    diagnostics,
                    diagnosticKeys,
                    cancellationToken)
                .ConfigureAwait(false);
            DiscoveredSourceFile[] projectSourceFiles = CreateProjectSourceFiles(sources, contexts);
            var externalWorkspaceSources = contexts.Values
                .SelectMany(context => context.SyntaxTreesByFullPath.Keys)
                .Where(path => !IsWithinRoot(sources.RootPath, path))
                .Where(path => !IsGeneratedWorkspaceSource(path))
                .Distinct(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var hasCompleteSemanticCoverage = externalWorkspaceSources.Length == 0;

            if (externalWorkspaceSources.Length > 0)
            {
                var examples = string.Join(", ", externalWorkspaceSources.Take(3).Select(Path.GetFileName));
                messages.Add(
                    $"MSBuildWorkspace loaded {externalWorkspaceSources.Length} authored source file(s) outside " +
                    $"the analysis root ({examples}); linked external sources are not included and semantic results are not trusted.");
            }

            foreach (WorkspaceDiagnostic workspaceDiagnostic in workspaceDiagnostics)
            {
                AnalysisDiagnosticCollector.Add(
                    diagnostics,
                    diagnosticKeys,
                    FromWorkspace(workspaceDiagnostic));
            }

            var workspaceHadFailures = workspaceDiagnostics.Any(
                diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure);
            if (workspaceHadFailures)
            {
                AddProjectLoadFailureDiagnostics(
                    sources,
                    diagnostics,
                    diagnosticKeys,
                    "MSBuildWorkspace reported project load failures; see workspace_load diagnostics for details.");
            }

            if (contexts.Count == 0)
            {
                messages.Add("MSBuildWorkspace did not load any project compilations; semantic analysis fell back to syntax-only parsing.");
                workspaceHadFailures = true;
            }

            return new SemanticLoadResult(
                contexts,
                projectSourceFiles,
                contexts.Count == 0 ? "not_available" : "msbuild",
                restore.Status,
                TrustDiagnostics: contexts.Count > 0 &&
                    hasCompleteSemanticCoverage &&
                    !workspaceHadFailures &&
                    restore.Status != "failed",
                WorkspaceHadFailures: workspaceHadFailures,
                messages);
        }
        catch (Exception exception) when (IsOperationalWorkspaceException(exception))
        {
            messages.Add($"MSBuildWorkspace failed: {exception.Message}");
            AddProjectLoadFailureDiagnostics(sources, diagnostics, diagnosticKeys, exception.Message);
            return Unavailable(restore.Status, messages);
        }
    }

    internal static bool IsOperationalWorkspaceException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException
            or (InvalidOperationException and not ObjectDisposedException);
    }

    public static AnalysisHealth CreateHealth(
        bool useSemantic,
        SemanticLoadResult semanticLoad,
        string? semanticInitializationFailure)
    {
        if (!useSemantic)
        {
            return new AnalysisHealth
            {
                AnalysisQuality = semanticInitializationFailure is null ? "syntax_only" : "degraded",
                SemanticModel = semanticInitializationFailure is null ? "none" : "not_available",
                RestoreStatus = "not_run",
                BuildStatus = "not_run",
                TrustedDiagnostics = false,
                DiagnosticsIncludedInHotspotRank = false,
                Messages = semanticLoad.Messages
            };
        }

        var trusted = semanticLoad.TrustDiagnostics;

        return new AnalysisHealth
        {
            AnalysisQuality = trusted ? "trusted" : "degraded",
            SemanticModel = semanticLoad.SemanticModel,
            RestoreStatus = semanticLoad.RestoreStatus,
            BuildStatus = "not_run",
            TrustedDiagnostics = trusted,
            DiagnosticsIncludedInHotspotRank = trusted,
            Messages = semanticLoad.Messages
        };
    }

    private static SemanticLoadResult Unavailable(
        string restoreStatus,
        IReadOnlyList<string> messages)
    {
        return new SemanticLoadResult(
            new Dictionary<string, ProjectSemanticContext>(StringComparer.Ordinal),
            [],
            "not_available",
            restoreStatus,
            TrustDiagnostics: false,
            WorkspaceHadFailures: true,
            messages);
    }

    private static Dictionary<string, string> CreateWorkspaceProperties()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["DesignTimeBuild"] = "true",
            ["BuildProjectReferences"] = "false",
            ["SkipCompilerExecution"] = "true",
            ["ProvideCommandLineArgs"] = "true",
            ["CopyBuildOutputToOutputDirectory"] = "false",
            ["CopyOutputSymbolsToOutputDirectory"] = "false",
            ["CopyDocumentationFileToOutputDirectory"] = "false"
        };
    }

    private static async Task<Solution> LoadSolutionAsync(
        DiscoveredSources sources,
        MSBuildWorkspace workspace,
        CancellationToken cancellationToken)
    {
        if (sources.SelectedSolutionPath is not null)
        {
            return await workspace.OpenSolutionAsync(
                    Path.Combine(sources.RootPath, sources.SelectedSolutionPath),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var projectPath in sources.ProjectPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fullProjectPath = NormalizeFullPath(Path.Combine(sources.RootPath, projectPath));
            var alreadyLoaded = workspace.CurrentSolution.Projects.Any(project =>
                project.FilePath is not null &&
                string.Equals(
                    NormalizeFullPath(project.FilePath),
                    fullProjectPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal));
            if (alreadyLoaded)
            {
                continue;
            }

            await workspace.OpenProjectAsync(
                    fullProjectPath,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }

        return workspace.CurrentSolution;
    }

    private static async Task<Dictionary<string, ProjectSemanticContext>> CreateProjectContextsAsync(
        string rootPath,
        Solution solution,
        IReadOnlySet<string> includedSourcePaths,
        bool collectCompilationDiagnostics,
        List<AnalysisDiagnostic> diagnostics,
        HashSet<string> diagnosticKeys,
        CancellationToken cancellationToken)
    {
        var contexts = new Dictionary<string, ProjectSemanticContext>(StringComparer.Ordinal);

        foreach (Project project in solution.Projects.Where(project => project.Language == LanguageNames.CSharp))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var projectRelativePath = ToRelativeProjectPath(rootPath, project.FilePath);
            if (projectRelativePath is null)
            {
                continue;
            }

            Compilation? compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is not CSharpCompilation csharpCompilation)
            {
                continue;
            }

            var syntaxTreesByFullPath = csharpCompilation.SyntaxTrees
                .Where(tree => !string.IsNullOrWhiteSpace(tree.FilePath))
                .GroupBy(tree => NormalizeFullPath(tree.FilePath), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            contexts[projectRelativePath] = new ProjectSemanticContext(
                project.AssemblyName ?? ResolveAssemblyName(projectRelativePath),
                csharpCompilation,
                syntaxTreesByFullPath);
            if (collectCompilationDiagnostics)
            {
                AddCompilationDiagnostics(
                    rootPath,
                    projectRelativePath,
                    csharpCompilation,
                    includedSourcePaths,
                    diagnostics,
                    diagnosticKeys,
                    cancellationToken);
            }
        }

        return contexts;
    }

    private static DiscoveredSourceFile[] CreateProjectSourceFiles(
        DiscoveredSources sources,
        IReadOnlyDictionary<string, ProjectSemanticContext> contexts)
    {
        var candidatesByFullPath = sources.CandidateSourceFiles
            .GroupBy(source => NormalizeFullPath(source.FullPath), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var selectedProjects = sources.ProjectPaths.ToHashSet(StringComparer.Ordinal);

        return contexts
            .Where(entry => selectedProjects.Contains(entry.Key))
            .SelectMany(entry => entry.Value.SyntaxTreesByFullPath.Keys
                .Where(candidatesByFullPath.ContainsKey)
                .Select(fullPath => candidatesByFullPath[fullPath] with
                {
                    ProjectPath = entry.Key,
                    ProjectKey = ProjectIdentity.Key(entry.Key)
                }))
            .DistinctBy(
                source => (source.ProjectKey, source.RelativePath),
                EqualityComparer<(string ProjectKey, string RelativePath)>.Default)
            .OrderBy(source => source.ProjectPath, StringComparer.Ordinal)
            .ThenBy(source => source.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> DetectAmbientBuildFiles(string rootPath)
    {
        var root = new DirectoryInfo(rootPath);

        for (DirectoryInfo? directory = root.Parent; directory is not null; directory = directory.Parent)
        {
            foreach (var fileName in new[] { "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "NuGet.config", "nuget.config" })
            {
                var candidate = Path.Combine(directory.FullName, fileName);
                if (File.Exists(candidate))
                {
                    yield return $"Ambient MSBuild/NuGet file outside analyzed root may affect project evaluation: {candidate}";
                }
            }
        }
    }

    private static void AddProjectLoadFailureDiagnostics(
        DiscoveredSources sources,
        List<AnalysisDiagnostic> diagnostics,
        HashSet<string> diagnosticKeys,
        string message)
    {
        foreach (var projectPath in sources.ProjectPaths.DefaultIfEmpty(sources.RootPath))
        {
            AnalysisDiagnosticCollector.Add(diagnostics, diagnosticKeys, new AnalysisDiagnostic
            {
                Id = "project_load_failed",
                Severity = "critical",
                Message = message,
                ProjectPath = projectPath,
                FilePath = projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ? projectPath : null,
                StartLine = null,
                EndLine = null,
                Tags = ["project_load"]
            });
        }
    }

    private static AnalysisDiagnostic FromWorkspace(WorkspaceDiagnostic diagnostic)
    {
        return new AnalysisDiagnostic
        {
            Id = diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? "workspace_load_failed" : "workspace_load_warning",
            Severity = diagnostic.Kind == WorkspaceDiagnosticKind.Failure ? "error" : "warning",
            Message = diagnostic.Message,
            ProjectPath = null,
            FilePath = null,
            StartLine = null,
            EndLine = null,
            Tags = ["workspace_load"]
        };
    }

    private static string? ToRelativeProjectPath(string rootPath, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        var relativePath = Path.GetRelativePath(rootPath, projectPath)
            .Replace(Path.DirectorySeparatorChar, '/');

        return string.Equals(relativePath, "..", StringComparison.Ordinal) ||
            relativePath.StartsWith("../", StringComparison.Ordinal)
                ? null
                : relativePath;
    }

    private static string NormalizeFullPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsWithinRoot(string rootPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, candidatePath)
            .Replace(Path.DirectorySeparatorChar, '/');

        return !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
            !relativePath.StartsWith("../", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relativePath);
    }

    private static bool IsGeneratedWorkspaceSource(string path)
    {
        var fileName = Path.GetFileName(path);

        return fileName.EndsWith(".g.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".generated.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".designer.cs", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Microsoft.NET.Test.Sdk.Program.cs", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(fileName, "AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddCompilationDiagnostics(
        string rootPath,
        string projectPath,
        CSharpCompilation compilation,
        IReadOnlySet<string> includedSourcePaths,
        List<AnalysisDiagnostic> diagnostics,
        HashSet<string> diagnosticKeys,
        CancellationToken cancellationToken)
    {
        foreach (Diagnostic diagnostic in compilation.GetDiagnostics(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSyntheticImplicitUsingsDiagnostic(diagnostic))
            {
                continue;
            }

            if (diagnostic.Location.IsInSource &&
                diagnostic.Location.SourceTree?.FilePath is { Length: > 0 } diagnosticFilePath &&
                !includedSourcePaths.Contains(NormalizeFullPath(diagnosticFilePath)))
            {
                continue;
            }

            AnalysisDiagnosticCollector.Add(
                diagnostics,
                diagnosticKeys,
                AnalysisDiagnosticCollector.FromCompilation(rootPath, projectPath, diagnostic));
        }
    }

    private static bool IsSyntheticImplicitUsingsDiagnostic(Diagnostic diagnostic)
    {
        return diagnostic.Location.SourceTree?.FilePath.EndsWith(".ImplicitUsings.g.cs", StringComparison.Ordinal) == true;
    }

    private static string ResolveAssemblyName(string projectPath)
    {
        if (ProjectIdentity.IsSynthetic(projectPath))
        {
            return ProjectIdentity.SyntheticProjectName;
        }

        return Path.GetFileNameWithoutExtension(projectPath);
    }
}
