using System.Globalization;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Syntax;

public static class SyntaxFactsCollector
{
    private const string SemanticStability = "semantic";
    private const string SyntaxFallbackStability = "syntax_fallback";

    public static async Task<SyntaxAnalysisFacts> CollectAsync(
        DiscoveredSources sources,
        bool useSemantic,
        bool noRestore,
        CancellationToken cancellationToken,
        string? semanticInitializationFailure = null)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var diagnostics = new List<AnalysisDiagnostic>();
        var diagnosticKeys = new HashSet<string>(StringComparer.Ordinal);
        SemanticLoadResult semanticLoad = useSemantic
            ? await CreateSemanticContextsAsync(sources, noRestore, diagnostics, diagnosticKeys, cancellationToken)
                .ConfigureAwait(false)
            : SemanticLoadResult.SyntaxOnly(
                semanticInitializationFailure ?? "Syntax-only analysis was requested.");
        List<SourceFileContext> sourceFiles = await ParseSourceFilesAsync(
                sources.SourceFiles,
                semanticLoad.Contexts,
                diagnostics,
                diagnosticKeys,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<FileFacts> files = sourceFiles
            .Select(context => CreateFileFacts(context))
            .OrderBy(file => file.FilePath, StringComparer.Ordinal)
            .ToArray();

        List<TypeDeclarationInfo> typeDeclarations = CollectTypeDeclarations(
            sourceFiles,
            semanticLoad.Contexts,
            cancellationToken);
        Dictionary<string, TypeFacts> typeFactsById = CreateTypeFacts(
            typeDeclarations,
            cancellationToken);
        IReadOnlyList<TypeFacts> types = typeFactsById.Values
            .OrderBy(type => type.TargetId, StringComparer.Ordinal)
            .ToArray();

        List<MemberDeclarationInfo> memberDeclarations = CollectMemberDeclarations(
            typeDeclarations,
            typeFactsById,
            cancellationToken);
        Dictionary<string, MemberFacts> memberFactsById = CreateMemberFacts(memberDeclarations);
        IReadOnlyList<MemberFacts> members = memberFactsById.Values
            .OrderBy(member => member.TargetId, StringComparer.Ordinal)
            .ToArray();

        GraphEdgeFacts[] graphEdges = CreateGraphEdges(
            typeDeclarations,
            memberDeclarations,
            memberFactsById,
            cancellationToken);

        return new SyntaxAnalysisFacts
        {
            Mode = DetermineMode(typeDeclarations, memberDeclarations),
            Health = CreateHealth(useSemantic, semanticLoad, semanticInitializationFailure),
            RootPath = sources.RootPath,
            ProjectPaths = sources.ProjectPaths,
            Files = files,
            Types = types,
            Members = members,
            GraphEdges = graphEdges,
            Diagnostics = diagnostics
                .OrderBy(diagnostic => diagnostic.FilePath, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.StartLine)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static async Task<List<SourceFileContext>> ParseSourceFilesAsync(
        IReadOnlyList<DiscoveredSourceFile> sourceFiles,
        IReadOnlyDictionary<string, ProjectSemanticContext> semanticContexts,
        List<AnalysisDiagnostic> diagnostics,
        HashSet<string> diagnosticKeys,
        CancellationToken cancellationToken)
    {
        var contexts = new List<SourceFileContext>();

        foreach (DiscoveredSourceFile sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SyntaxTree syntaxTree;
            SourceText sourceText;
            SyntaxNode root;

            if (semanticContexts.TryGetValue(sourceFile.ProjectPath, out ProjectSemanticContext? semanticContext) &&
                semanticContext.SyntaxTreesByFullPath.TryGetValue(NormalizeFullPath(sourceFile.FullPath), out SyntaxTree? workspaceSyntaxTree))
            {
                syntaxTree = workspaceSyntaxTree;
                sourceText = await syntaxTree.GetTextAsync(cancellationToken).ConfigureAwait(false);
                root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                sourceText = SourceText.From(await File.ReadAllTextAsync(sourceFile.FullPath, cancellationToken).ConfigureAwait(false));
                syntaxTree = CSharpSyntaxTree.ParseText(
                    sourceText,
                    CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
                    path: sourceFile.FullPath,
                    cancellationToken: cancellationToken);
                root = await syntaxTree.GetRootAsync(cancellationToken).ConfigureAwait(false);
            }

            var context = new SourceFileContext(sourceFile, sourceText, syntaxTree, root);
            contexts.Add(context);

            foreach (Diagnostic diagnostic in syntaxTree.GetDiagnostics(cancellationToken))
            {
                AddDiagnostic(
                    diagnostics,
                    diagnosticKeys,
                    ToSyntaxDiagnostic(diagnostic, sourceFile));
            }
        }

        return contexts;
    }

    private static async Task<SemanticLoadResult> CreateSemanticContextsAsync(
        DiscoveredSources sources,
        bool noRestore,
        List<AnalysisDiagnostic> diagnostics,
        HashSet<string> diagnosticKeys,
        CancellationToken cancellationToken)
    {
        var messages = DetectAmbientBuildFiles(sources.RootPath).ToList();
        RestoreResult restore = await RestoreProjectsAsync(sources, noRestore, cancellationToken).ConfigureAwait(false);
        messages.AddRange(restore.Messages);
        if (restore.Status == "failed")
        {
            messages.Add("Compiler diagnostics were suppressed because restore failed; emitted diagnostics are not treated as code-quality metrics.");
        }

        if (sources.ProjectPaths.Count == 0)
        {
            messages.Add("No C# project files were discovered; semantic analysis fell back to syntax-only parsing.");
            return new SemanticLoadResult(new Dictionary<string, ProjectSemanticContext>(StringComparer.Ordinal), "not_available", restore.Status, TrustDiagnostics: false, WorkspaceHadFailures: true, messages);
        }

        try
        {
            using var workspace = MSBuildWorkspace.Create(CreateWorkspaceProperties());
            workspace.SkipUnrecognizedProjects = true;
            workspace.LoadMetadataForReferencedProjects = false;

            var workspaceDiagnostics = new List<WorkspaceDiagnostic>();
            workspace.RegisterWorkspaceFailedHandler(args => workspaceDiagnostics.Add(args.Diagnostic));

            Solution solution = await LoadWorkspaceSolutionAsync(sources, workspace, cancellationToken).ConfigureAwait(false);
            IReadOnlySet<string> includedSourcePaths = sources.SourceFiles
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
            var coveredSourceCount = sources.SourceFiles.Count(source =>
                contexts.TryGetValue(source.ProjectPath, out ProjectSemanticContext? context) &&
                context.SyntaxTreesByFullPath.ContainsKey(NormalizeFullPath(source.FullPath)));
            var externalWorkspaceSources = contexts.Values
                .SelectMany(context => context.SyntaxTreesByFullPath.Keys)
                .Where(path => !IsWithinRoot(sources.RootPath, path))
                .Where(path => !IsGeneratedWorkspaceSource(path))
                .Distinct(OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var hasCompleteSemanticCoverage = coveredSourceCount == sources.SourceFiles.Count &&
                externalWorkspaceSources.Length == 0;

            if (!hasCompleteSemanticCoverage)
            {
                messages.Add(
                    $"Semantic analysis covered {coveredSourceCount} of {sources.SourceFiles.Count} discovered source files; " +
                    "compiler diagnostics and semantic metrics are not trusted for the complete input scope.");
            }

            if (externalWorkspaceSources.Length > 0)
            {
                var examples = string.Join(
                    ", ",
                    externalWorkspaceSources
                        .Take(3)
                        .Select(Path.GetFileName));
                messages.Add(
                    $"MSBuildWorkspace loaded {externalWorkspaceSources.Length} authored source file(s) outside " +
                    $"the analysis root ({examples}); linked external sources are not included and semantic results are not trusted.");
            }

            foreach (WorkspaceDiagnostic workspaceDiagnostic in workspaceDiagnostics)
            {
                AddDiagnostic(diagnostics, diagnosticKeys, ToWorkspaceDiagnostic(workspaceDiagnostic));
            }

            var workspaceHadFailures = workspaceDiagnostics.Any(diagnostic => diagnostic.Kind == WorkspaceDiagnosticKind.Failure);
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
                contexts.Count == 0 ? "not_available" : "msbuild",
                restore.Status,
                TrustDiagnostics: contexts.Count > 0 &&
                    hasCompleteSemanticCoverage &&
                    !workspaceHadFailures &&
                    restore.Status != "failed",
                WorkspaceHadFailures: workspaceHadFailures,
                messages);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            messages.Add($"MSBuildWorkspace failed: {exception.Message}");
            AddProjectLoadFailureDiagnostics(sources, diagnostics, diagnosticKeys, exception.Message);

            return new SemanticLoadResult(new Dictionary<string, ProjectSemanticContext>(StringComparer.Ordinal), "not_available", restore.Status, TrustDiagnostics: false, WorkspaceHadFailures: true, messages);
        }
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

    private static async Task<Solution> LoadWorkspaceSolutionAsync(
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

            var fullProjectPath = NormalizeFullPath(
                Path.Combine(sources.RootPath, projectPath));
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

    private static async Task<RestoreResult> RestoreProjectsAsync(
        DiscoveredSources sources,
        bool noRestore,
        CancellationToken cancellationToken)
    {
        if (noRestore)
        {
            return new RestoreResult("not_run", ["Restore was skipped because --no-restore was specified."]);
        }

        var targetPaths = SelectRestoreTargets(sources);
        if (targetPaths.Length == 0)
        {
            return new RestoreResult("not_run", ["Restore was skipped because no solution or project file was discovered."]);
        }

        var messages = new List<string>();

        foreach (var targetPath in targetPaths)
        {
            ProcessResult result = await RunProcessAsync(
                    "dotnet",
                    ["restore", targetPath],
                    sources.RootPath,
                    cancellationToken)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                messages.Add($"dotnet restore failed for {Path.GetFileName(targetPath)} with exit code {result.ExitCode}: {TrimProcessOutput(result)}");
                return new RestoreResult("failed", messages);
            }

            messages.Add($"dotnet restore succeeded for {Path.GetFileName(targetPath)}.");
        }

        return new RestoreResult("success", messages);
    }

    private static string[] SelectRestoreTargets(DiscoveredSources sources)
    {
        if (sources.SelectedSolutionPath is not null)
        {
            return [Path.Combine(sources.RootPath, sources.SelectedSolutionPath)];
        }

        return sources.ProjectPaths
            .Select(projectPath => Path.Combine(sources.RootPath, projectPath))
            .ToArray();
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false
            }
        };

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        cancellationToken.ThrowIfCancellationRequested();
        process.Start();
        Task<string> output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        Task<string> error = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using CancellationTokenRegistration cancellationRegistration = cancellationToken.Register(
            static state => TryKillProcessTree((Process)state!),
            process);

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await ObserveCanceledProcessAsync(process, output, error).ConfigureAwait(false);
            throw;
        }

        return new ProcessResult(
            process.ExitCode,
            await output.ConfigureAwait(false),
            await error.ConfigureAwait(false));
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the HasExited check and Kill.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Cancellation still propagates if the operating system denies the kill request.
        }
    }

    private static async Task ObserveCanceledProcessAsync(
        Process process,
        Task<string> output,
        Task<string> error)
    {
        try
        {
            await Task.WhenAll(
                    process.WaitForExitAsync(CancellationToken.None),
                    output,
                    error)
                .WaitAsync(TimeSpan.FromSeconds(5))
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is
            IOException or
            InvalidOperationException or
            ObjectDisposedException or
            TimeoutException)
        {
            // Best-effort cleanup must not replace the original cancellation exception.
        }
    }

    private static string TrimProcessOutput(ProcessResult result)
    {
        var text = string.IsNullOrWhiteSpace(result.StandardError)
            ? result.StandardOutput
            : result.StandardError;
        var flattened = string.Join(" ", text
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(4));

        return string.IsNullOrWhiteSpace(flattened) ? "no process output" : flattened;
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
            AddDiagnostic(diagnostics, diagnosticKeys, new AnalysisDiagnostic
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

    private static AnalysisDiagnostic ToWorkspaceDiagnostic(WorkspaceDiagnostic diagnostic)
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

    private static AnalysisHealth CreateHealth(
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

            AddDiagnostic(
                diagnostics,
                diagnosticKeys,
                ToCompilationDiagnostic(rootPath, projectPath, diagnostic));
        }
    }

    private static bool IsSyntheticImplicitUsingsDiagnostic(Diagnostic diagnostic)
    {
        return diagnostic.Location.SourceTree?.FilePath.EndsWith(".ImplicitUsings.g.cs", StringComparison.Ordinal) == true;
    }

    private static List<TypeDeclarationInfo> CollectTypeDeclarations(
        IReadOnlyList<SourceFileContext> sourceFiles,
        IReadOnlyDictionary<string, ProjectSemanticContext> semanticContexts,
        CancellationToken cancellationToken)
    {
        var typeDeclarations = new List<TypeDeclarationInfo>();

        foreach (SourceFileContext context in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var treeBelongsToCompilation = semanticContexts.TryGetValue(context.SourceFile.ProjectPath, out ProjectSemanticContext? project) &&
                project.SyntaxTreesByFullPath.ContainsKey(NormalizeFullPath(context.SyntaxTree.FilePath));
            SemanticModel? semanticModel = treeBelongsToCompilation
                ? project!.Compilation.GetSemanticModel(context.SyntaxTree, ignoreAccessibility: true)
                : null;
            var assemblyName = project?.AssemblyName ?? ResolveAssemblyName(context.SourceFile.ProjectPath);

            foreach (BaseTypeDeclarationSyntax typeDeclaration in context.Root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                typeDeclarations.Add(CreateTypeDeclarationInfo(
                    typeDeclaration,
                    context,
                    semanticModel,
                    assemblyName));
            }
        }

        return typeDeclarations;
    }

    private static TypeDeclarationInfo CreateTypeDeclarationInfo(
        BaseTypeDeclarationSyntax typeDeclaration,
        SourceFileContext context,
        SemanticModel? semanticModel,
        string fallbackAssemblyName)
    {
        INamedTypeSymbol? symbol = semanticModel?.GetDeclaredSymbol(typeDeclaration);
        var fallbackName = BuildQualifiedTypeName(typeDeclaration);
        var name = symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? fallbackName;
        var documentationCommentId = GetDocumentationCommentId(symbol);
        var assemblyName = symbol?.ContainingAssembly?.Name ?? fallbackAssemblyName;
        var targetId = documentationCommentId is null || symbol is null
            ? TargetIds.Type(context.SourceFile.ProjectKey, fallbackName, context.SourceFile.RelativePath)
            : TargetIds.TypeSemantic(assemblyName, documentationCommentId);
        var targetIdStability = documentationCommentId is null ? SyntaxFallbackStability : SemanticStability;
        TextSpan span = typeDeclaration.Span;
        FileLinePositionSpan lineSpan = context.SyntaxTree.GetLineSpan(span);

        return new TypeDeclarationInfo(
            typeDeclaration,
            context,
            semanticModel,
            symbol,
            targetId,
            targetIdStability,
            context.SourceFile.ProjectKey,
            name,
            context.SourceFile.RelativePath,
            ToOneBasedLine(lineSpan.StartLinePosition.Line),
            ToOneBasedLine(lineSpan.EndLinePosition.Line),
            CountLines(span, context.SyntaxTree),
            CountTokenLines(span, context.Root, context.SyntaxTree),
            GetSupportedMembers(typeDeclaration).Count());
    }

    private static Dictionary<string, TypeFacts> CreateTypeFacts(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        CancellationToken cancellationToken)
    {
        return typeDeclarations
            .GroupBy(declaration => declaration.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    TypeDeclarationInfo[] declarations = group
                        .OrderBy(declaration => declaration.FilePath, StringComparer.Ordinal)
                        .ThenBy(declaration => declaration.StartLine)
                        .ToArray();
                    TypeDeclarationInfo first = declarations[0];

                    return new TypeFacts
                    {
                        TargetId = group.Key,
                        TargetIdStability = first.TargetIdStability,
                        ParentFileTargetId = TargetIds.File(first.FilePath),
                        ProjectKey = first.ProjectKey,
                        Name = first.Name,
                        FilePath = first.FilePath,
                        StartLine = first.StartLine,
                        EndLine = first.EndLine,
                        Declarations = declarations
                            .Select(declaration => new SourceSpanFacts
                            {
                                FilePath = declaration.FilePath,
                                StartLine = declaration.StartLine,
                                EndLine = declaration.EndLine
                            })
                            .ToArray(),
                        LinesOfCode = declarations.Sum(declaration => declaration.LinesOfCode),
                        NonCommentLinesOfCode = declarations.Sum(declaration => declaration.NonCommentLinesOfCode),
                        MemberCount = declarations.Sum(declaration => declaration.MemberCount),
                        Semantic = CreateTypeSemanticFacts(declarations, cancellationToken)
                    };
                },
                StringComparer.Ordinal);
    }

    private static TypeSemanticFacts? CreateTypeSemanticFacts(
        IReadOnlyList<TypeDeclarationInfo> declarations,
        CancellationToken cancellationToken)
    {
        SemanticTypeDeclaration[] semanticDeclarations = declarations
            .Where(declaration => declaration.Symbol is not null && declaration.SemanticModel is not null)
            .Select(declaration => new SemanticTypeDeclaration(
                declaration.Declaration,
                declaration.SemanticModel!,
                declaration.Symbol!))
            .ToArray();

        return TypeSemanticFactsCollector.Collect(
            semanticDeclarations,
            cancellationToken);
    }

    private static List<MemberDeclarationInfo> CollectMemberDeclarations(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        Dictionary<string, TypeFacts> typeFactsById,
        CancellationToken cancellationToken)
    {
        var memberDeclarations = new List<MemberDeclarationInfo>();

        foreach (TypeDeclarationInfo typeDeclaration in typeDeclarations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TypeFacts parentType = typeFactsById[typeDeclaration.TargetId];

            foreach (MemberDeclarationSyntax memberDeclaration in GetSupportedMembers(typeDeclaration.Declaration))
            {
                memberDeclarations.Add(CreateMemberDeclarationInfo(
                    memberDeclaration,
                    parentType,
                    typeDeclaration.Context,
                    typeDeclaration.SemanticModel,
                    cancellationToken));
            }
        }

        return memberDeclarations;
    }

    private static MemberDeclarationInfo CreateMemberDeclarationInfo(
        MemberDeclarationSyntax memberDeclaration,
        TypeFacts parentType,
        SourceFileContext context,
        SemanticModel? semanticModel,
        CancellationToken cancellationToken)
    {
        ISymbol? symbol = GetDeclaredSymbol(memberDeclaration, semanticModel);
        var memberName = GetMemberName(memberDeclaration);
        var parameterCount = GetParameterCount(memberDeclaration);
        FileLinePositionSpan lineSpan = context.SyntaxTree.GetLineSpan(memberDeclaration.Span, cancellationToken);
        var startLine = ToOneBasedLine(lineSpan.StartLinePosition.Line);
        var endLine = ToOneBasedLine(lineSpan.EndLinePosition.Line);
        var documentationCommentId = GetDocumentationCommentId(symbol);
        var assemblyName = symbol?.ContainingAssembly?.Name ?? ResolveAssemblyName(context.SourceFile.ProjectPath);
        var targetId = documentationCommentId is null || symbol is null
            ? TargetIds.Member(
                context.SourceFile.ProjectKey,
                parentType.Name,
                memberName,
                parameterCount,
                context.SourceFile.RelativePath,
                startLine)
            : TargetIds.MemberSemantic(assemblyName, documentationCommentId);
        var targetIdStability = documentationCommentId is null ? SyntaxFallbackStability : SemanticStability;
        ControlFlowFacts controlFlowFacts = ControlFlowFactsCollector.Collect(memberDeclaration, cancellationToken);
        OperationFacts? operationFacts = semanticModel is null
            ? null
            : OperationFactsCollector.Collect(memberDeclaration, semanticModel, cancellationToken);

        return new MemberDeclarationInfo(
            memberDeclaration,
            parentType.TargetId,
            context,
            semanticModel,
            symbol,
            targetId,
            targetIdStability,
            context.SourceFile.ProjectKey,
            $"{parentType.Name}.{memberName}",
            GetChunkKind(memberDeclaration),
            context.SourceFile.RelativePath,
            startLine,
            endLine,
            HasBody(memberDeclaration),
            controlFlowFacts,
            operationFacts,
            Math.Max(1, endLine - startLine + 1),
            parameterCount);
    }

    private static Dictionary<string, MemberFacts> CreateMemberFacts(IReadOnlyList<MemberDeclarationInfo> memberDeclarations)
    {
        return memberDeclarations
            .GroupBy(declaration => declaration.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    MemberDeclarationInfo primary = group
                        .OrderByDescending(declaration => declaration.HasBody)
                        .ThenBy(declaration => declaration.FilePath, StringComparer.Ordinal)
                        .ThenBy(declaration => declaration.StartLine)
                        .First();

                    return new MemberFacts
                    {
                        TargetId = group.Key,
                        TargetIdStability = primary.TargetIdStability,
                        ParentTypeTargetId = primary.ParentTypeTargetId,
                        ProjectKey = primary.ProjectKey,
                        Name = primary.Name,
                        ChunkKind = primary.ChunkKind,
                        FilePath = primary.FilePath,
                        StartLine = primary.StartLine,
                        EndLine = primary.EndLine,
                        Declarations = group
                            .OrderBy(declaration => declaration.FilePath, StringComparer.Ordinal)
                            .ThenBy(declaration => declaration.StartLine)
                            .Select(declaration => new SourceSpanFacts
                            {
                                FilePath = declaration.FilePath,
                                StartLine = declaration.StartLine,
                                EndLine = declaration.EndLine
                            })
                            .ToArray(),
                        ControlFlow = primary.ControlFlow,
                        Operations = primary.Operations,
                        MethodLength = primary.MethodLength,
                        ParameterCount = primary.ParameterCount
                    };
                },
                StringComparer.Ordinal);
    }

    private static GraphEdgeFacts[] CreateGraphEdges(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        IReadOnlyList<MemberDeclarationInfo> memberDeclarations,
        Dictionary<string, MemberFacts> memberFactsById,
        CancellationToken cancellationToken)
    {
        var edges = new List<GraphEdgeFacts>();
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);
        var typeTargetsBySymbol = typeDeclarations
            .Select(declaration => new
            {
                Key = GetSymbolMapKey(declaration.Symbol),
                declaration.TargetId
            })
            .Where(entry => entry.Key is not null)
            .GroupBy(entry => entry.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().TargetId, StringComparer.Ordinal);
        var memberTargetsBySymbol = memberDeclarations
            .Select(declaration => new
            {
                Key = GetSymbolMapKey(declaration.Symbol),
                TargetId = memberFactsById[declaration.TargetId].TargetId
            })
            .Where(entry => entry.Key is not null)
            .GroupBy(entry => entry.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().TargetId, StringComparer.Ordinal);

        foreach (TypeDeclarationInfo typeDeclaration in typeDeclarations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            AddEdge(
                edges,
                seenEdges,
                TargetIds.File(typeDeclaration.FilePath),
                typeDeclaration.TargetId,
                "declares",
                "exact");

            if (typeDeclaration.Symbol is null)
            {
                continue;
            }

            if (TryGetTypeTarget(typeDeclaration.Symbol.BaseType, typeTargetsBySymbol, out var baseTypeTargetId) &&
                !IsObject(typeDeclaration.Symbol.BaseType))
            {
                AddEdge(edges, seenEdges, typeDeclaration.TargetId, baseTypeTargetId, "inherits", "exact");
            }

            foreach (INamedTypeSymbol interfaceSymbol in typeDeclaration.Symbol.Interfaces)
            {
                if (TryGetTypeTarget(interfaceSymbol, typeTargetsBySymbol, out var interfaceTargetId))
                {
                    AddEdge(edges, seenEdges, typeDeclaration.TargetId, interfaceTargetId, "implements", "exact");
                }
            }
        }

        foreach (MemberDeclarationInfo memberDeclaration in memberDeclarations)
        {
            cancellationToken.ThrowIfCancellationRequested();

            MemberFacts member = memberFactsById[memberDeclaration.TargetId];
            AddEdge(
                edges,
                seenEdges,
                member.ParentTypeTargetId,
                member.TargetId,
                "contains",
                "exact");

            if (memberDeclaration.SemanticModel is null)
            {
                continue;
            }

            AddUsesTypeEdges(memberDeclaration, member, typeTargetsBySymbol, edges, seenEdges, cancellationToken);
            AddCallEdges(memberDeclaration, member, memberTargetsBySymbol, edges, seenEdges, cancellationToken);
        }

        return edges
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddUsesTypeEdges(
        MemberDeclarationInfo memberDeclaration,
        MemberFacts member,
        IReadOnlyDictionary<string, string> typeTargetsBySymbol,
        List<GraphEdgeFacts> edges,
        HashSet<string> seenEdges,
        CancellationToken cancellationToken)
    {
        foreach (TypeSyntax typeSyntax in memberDeclaration.Declaration.DescendantNodes().OfType<TypeSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            ITypeSymbol? type = memberDeclaration.SemanticModel!.GetTypeInfo(typeSyntax, cancellationToken).Type;
            if (TryGetTypeTarget(type, typeTargetsBySymbol, out var typeTargetId) &&
                typeTargetId != member.ParentTypeTargetId)
            {
                AddEdge(edges, seenEdges, member.TargetId, typeTargetId, "uses_type", "exact");
            }
        }
    }

    private static void AddCallEdges(
        MemberDeclarationInfo memberDeclaration,
        MemberFacts member,
        IReadOnlyDictionary<string, string> memberTargetsBySymbol,
        List<GraphEdgeFacts> edges,
        HashSet<string> seenEdges,
        CancellationToken cancellationToken)
    {
        foreach (InvocationExpressionSyntax invocation in memberDeclaration.Declaration.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            ISymbol? symbol = memberDeclaration.SemanticModel!.GetSymbolInfo(invocation, cancellationToken).Symbol;
            if (TryGetMemberTarget(symbol, memberTargetsBySymbol, out var targetId))
            {
                AddEdge(edges, seenEdges, member.TargetId, targetId, "calls", "exact");
            }
        }

        foreach (ObjectCreationExpressionSyntax objectCreation in memberDeclaration.Declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            ISymbol? symbol = memberDeclaration.SemanticModel!.GetSymbolInfo(objectCreation, cancellationToken).Symbol;
            if (TryGetMemberTarget(symbol, memberTargetsBySymbol, out var targetId))
            {
                AddEdge(edges, seenEdges, member.TargetId, targetId, "calls", "exact");
            }
        }
    }

    private static void AddEdge(
        List<GraphEdgeFacts> edges,
        HashSet<string> seenEdges,
        string from,
        string to,
        string kind,
        string confidence)
    {
        var key = $"{from}\n{to}\n{kind}\n{confidence}";

        if (seenEdges.Add(key))
        {
            edges.Add(new GraphEdgeFacts
            {
                From = from,
                To = to,
                Kind = kind,
                Confidence = confidence
            });
        }
    }

    private static FileFacts CreateFileFacts(SourceFileContext context)
    {
        var fullSpan = new TextSpan(0, context.SourceText.Length);
        LineFacts lineCounts = LineFactsCollector.Collect(
            context.SyntaxTree,
            context.Root,
            context.SourceText);

        return new FileFacts
        {
            TargetId = TargetIds.File(context.SourceFile.RelativePath),
            TargetIdStability = SyntaxFallbackStability,
            ProjectKey = context.SourceFile.ProjectKey,
            FilePath = context.SourceFile.RelativePath,
            StartLine = 1,
            EndLine = Math.Max(1, context.SourceText.Lines.Count),
            LinesOfCode = lineCounts.LinesOfCode,
            NonCommentLinesOfCode = lineCounts.NonCommentLinesOfCode,
            BlankLineCount = lineCounts.BlankLineCount,
            CommentOnlyLineCount = lineCounts.CommentOnlyLineCount,
            CommentedLineCount = lineCounts.CommentedLineCount,
            MixedCodeCommentLineCount = lineCounts.MixedCodeCommentLineCount,
            DocumentationCommentLineCount = lineCounts.DocumentationCommentLineCount
        };
    }

    private static PortableExecutableReference[] CreateDefaultReferences()
    {
        var trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            return [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)];
        }

        return trustedPlatformAssemblies
            .Split(Path.PathSeparator)
            .Where(path => path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string ResolveAssemblyName(string projectPath)
    {
        if (projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(projectPath);
        }

        var directoryName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        return string.IsNullOrWhiteSpace(directoryName) ? "CodeMetricsProject" : directoryName;
    }

    private static IEnumerable<MemberDeclarationSyntax> GetSupportedMembers(BaseTypeDeclarationSyntax typeDeclaration)
    {
        return typeDeclaration is TypeDeclarationSyntax type
            ? type.Members.Where(member =>
                member is MethodDeclarationSyntax ||
                member is ConstructorDeclarationSyntax ||
                member is PropertyDeclarationSyntax ||
                member is IndexerDeclarationSyntax ||
                member is OperatorDeclarationSyntax ||
                member is ConversionOperatorDeclarationSyntax ||
                member is DestructorDeclarationSyntax)
            : [];
    }

    private static string BuildQualifiedTypeName(BaseTypeDeclarationSyntax typeDeclaration)
    {
        var parts = new Stack<string>();
        parts.Push(GetTypeName(typeDeclaration));

        for (SyntaxNode? node = typeDeclaration.Parent; node is not null; node = node.Parent)
        {
            switch (node)
            {
                case BaseTypeDeclarationSyntax parentType:
                    parts.Push(GetTypeName(parentType));
                    break;
                case BaseNamespaceDeclarationSyntax namespaceDeclaration:
                    parts.Push(namespaceDeclaration.Name.ToString());
                    break;
            }
        }

        return string.Join(".", parts);
    }

    private static string GetTypeName(BaseTypeDeclarationSyntax declaration)
    {
        var name = declaration.Identifier.ValueText;

        if (declaration is TypeDeclarationSyntax typeDeclaration && typeDeclaration.TypeParameterList is not null)
        {
            name += $"`{typeDeclaration.TypeParameterList.Parameters.Count}";
        }

        return name;
    }

    private static ISymbol? GetDeclaredSymbol(MemberDeclarationSyntax memberDeclaration, SemanticModel? semanticModel)
    {
        if (semanticModel is null)
        {
            return null;
        }

        return memberDeclaration switch
        {
            MethodDeclarationSyntax method => semanticModel.GetDeclaredSymbol(method),
            ConstructorDeclarationSyntax constructor => semanticModel.GetDeclaredSymbol(constructor),
            PropertyDeclarationSyntax property => semanticModel.GetDeclaredSymbol(property),
            IndexerDeclarationSyntax indexer => semanticModel.GetDeclaredSymbol(indexer),
            OperatorDeclarationSyntax operatorDeclaration => semanticModel.GetDeclaredSymbol(operatorDeclaration),
            ConversionOperatorDeclarationSyntax conversion => semanticModel.GetDeclaredSymbol(conversion),
            DestructorDeclarationSyntax destructor => semanticModel.GetDeclaredSymbol(destructor),
            _ => null
        };
    }

    private static string GetMemberName(MemberDeclarationSyntax memberDeclaration)
    {
        return memberDeclaration switch
        {
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax => "#ctor",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            IndexerDeclarationSyntax => "this[]",
            OperatorDeclarationSyntax operatorDeclaration => $"operator {operatorDeclaration.OperatorToken.ValueText}",
            ConversionOperatorDeclarationSyntax conversion => $"operator {conversion.Type}",
            DestructorDeclarationSyntax => "#dtor",
            _ => memberDeclaration.Kind().ToString()
        };
    }

    private static string GetChunkKind(MemberDeclarationSyntax memberDeclaration)
    {
        return memberDeclaration switch
        {
            ConstructorDeclarationSyntax => "constructor_body",
            PropertyDeclarationSyntax or IndexerDeclarationSyntax => "property_body",
            _ => "member_body"
        };
    }

    private static int GetParameterCount(MemberDeclarationSyntax memberDeclaration)
    {
        return memberDeclaration switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList.Parameters.Count,
            IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters.Count,
            _ => 0
        };
    }

    private static bool HasBody(MemberDeclarationSyntax memberDeclaration)
    {
        return memberDeclaration switch
        {
            BaseMethodDeclarationSyntax method => method.Body is not null || method.ExpressionBody is not null,
            PropertyDeclarationSyntax property => property.AccessorList is not null || property.ExpressionBody is not null,
            IndexerDeclarationSyntax indexer => indexer.AccessorList is not null || indexer.ExpressionBody is not null,
            _ => true
        };
    }

    private static int CountLines(TextSpan span, SyntaxTree syntaxTree)
    {
        FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(span);

        return Math.Max(1, lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line + 1);
    }

    private static int CountTokenLines(TextSpan span, SyntaxNode root, SyntaxTree syntaxTree)
    {
        var tokenLines = root
            .DescendantTokens(span)
            .Where(token => token.Span.Length > 0)
            .Select(token => syntaxTree.GetLineSpan(token.Span).StartLinePosition.Line)
            .ToHashSet();

        return tokenLines.Count;
    }

    private static int ToOneBasedLine(int zeroBasedLine)
    {
        return zeroBasedLine + 1;
    }

    private static AnalysisDiagnostic ToSyntaxDiagnostic(Diagnostic diagnostic, DiscoveredSourceFile sourceFile)
    {
        FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();

        return new AnalysisDiagnostic
        {
            Id = diagnostic.Id,
            Severity = ToSeverity(diagnostic.Severity),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            ProjectPath = sourceFile.ProjectPath,
            FilePath = sourceFile.RelativePath,
            StartLine = ToOneBasedLine(lineSpan.StartLinePosition.Line),
            EndLine = ToOneBasedLine(lineSpan.EndLinePosition.Line),
            Tags = ["syntax"]
        };
    }

    private static AnalysisDiagnostic ToCompilationDiagnostic(
        string rootPath,
        string projectPath,
        Diagnostic diagnostic)
    {
        FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
        var filePath = diagnostic.Location.SourceTree?.FilePath is { Length: > 0 } fullPath
            ? Path.GetRelativePath(rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/')
            : null;
        int? startLine = diagnostic.Location.IsInSource
            ? ToOneBasedLine(lineSpan.StartLinePosition.Line)
            : null;
        int? endLine = diagnostic.Location.IsInSource
            ? ToOneBasedLine(lineSpan.EndLinePosition.Line)
            : null;

        return new AnalysisDiagnostic
        {
            Id = diagnostic.Id,
            Severity = ToSeverity(diagnostic.Severity),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            ProjectPath = projectPath,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            Tags = CreateDiagnosticTags(diagnostic)
        };
    }

    private static List<string> CreateDiagnosticTags(Diagnostic diagnostic)
    {
        List<string> tags = diagnostic.Id.StartsWith("CS", StringComparison.Ordinal)
            ? ["compiler"]
            : ["analyzer"];

        if (IsNullableDiagnostic(diagnostic.Id))
        {
            tags.Add("nullable");
        }

        return tags;
    }

    private static bool IsNullableDiagnostic(string diagnosticId)
    {
        return diagnosticId.StartsWith("CS86", StringComparison.Ordinal) ||
            diagnosticId.StartsWith("CS87", StringComparison.Ordinal) ||
            diagnosticId.StartsWith("CS88", StringComparison.Ordinal);
    }

    private static void AddDiagnostic(
        List<AnalysisDiagnostic> diagnostics,
        HashSet<string> diagnosticKeys,
        AnalysisDiagnostic diagnostic)
    {
        var key = DiagnosticKey(diagnostic);

        if (diagnosticKeys.Add(key))
        {
            diagnostics.Add(diagnostic);
            return;
        }

        for (var index = 0; index < diagnostics.Count; index++)
        {
            AnalysisDiagnostic existing = diagnostics[index];
            if (!string.Equals(DiagnosticKey(existing), key, StringComparison.Ordinal))
            {
                continue;
            }

            var tags = MergeTags(existing.Tags, diagnostic.Tags);
            diagnostics[index] = existing with { Tags = tags };
            return;
        }
    }

    private static string DiagnosticKey(AnalysisDiagnostic diagnostic)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{diagnostic.Id}\n{diagnostic.ProjectPath}\n{diagnostic.FilePath}\n{diagnostic.StartLine}\n{diagnostic.EndLine}\n{diagnostic.Message}");
    }

    private static string[]? MergeTags(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        var tags = (left ?? [])
            .Concat(right ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return tags.Length == 0 ? null : tags;
    }

    private static string ToSeverity(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Hidden or DiagnosticSeverity.Info => "info",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Error => "error",
            _ => "warning"
        };
    }

    private static string DetermineMode(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        IReadOnlyList<MemberDeclarationInfo> memberDeclarations)
    {
        var hasSemanticIds = typeDeclarations.Any(declaration => declaration.TargetIdStability == SemanticStability) ||
            memberDeclarations.Any(declaration => declaration.TargetIdStability == SemanticStability);
        var hasFallbackIds = typeDeclarations.Any(declaration => declaration.TargetIdStability != SemanticStability) ||
            memberDeclarations.Any(declaration => declaration.TargetIdStability != SemanticStability);

        return hasSemanticIds switch
        {
            true when hasFallbackIds => "partial_semantic",
            true => "semantic",
            _ => "syntax"
        };
    }

    private static string? GetDocumentationCommentId(ISymbol? symbol)
    {
        return symbol?.OriginalDefinition.GetDocumentationCommentId();
    }

    private static string? GetSymbolMapKey(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return null;
        }

        var documentationCommentId = GetDocumentationCommentId(symbol);
        var assemblyName = symbol.OriginalDefinition.ContainingAssembly?.Name;

        return documentationCommentId is null || assemblyName is null
            ? null
            : $"{assemblyName}/{documentationCommentId}";
    }

    private static bool TryGetTypeTarget(
        ITypeSymbol? symbol,
        IReadOnlyDictionary<string, string> typeTargetsBySymbol,
        [NotNullWhen(true)]
        out string? targetId)
    {
        targetId = null;

        if (symbol is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var key = GetSymbolMapKey(namedType.OriginalDefinition);

        return key is not null && typeTargetsBySymbol.TryGetValue(key, out targetId);
    }

    private static bool TryGetMemberTarget(
        ISymbol? symbol,
        IReadOnlyDictionary<string, string> memberTargetsBySymbol,
        [NotNullWhen(true)]
        out string? targetId)
    {
        targetId = null;

        if (symbol is null)
        {
            return false;
        }

        var key = GetSymbolMapKey(symbol.OriginalDefinition);

        return key is not null && memberTargetsBySymbol.TryGetValue(key, out targetId);
    }

    private static bool IsObject(INamedTypeSymbol? symbol)
    {
        return symbol?.SpecialType == SpecialType.System_Object;
    }

    private sealed record SourceFileContext(
        DiscoveredSourceFile SourceFile,
        SourceText SourceText,
        SyntaxTree SyntaxTree,
        SyntaxNode Root);

    private sealed record ProjectSemanticContext(
        string AssemblyName,
        CSharpCompilation Compilation,
        IReadOnlyDictionary<string, SyntaxTree> SyntaxTreesByFullPath);

    private sealed record SemanticLoadResult(
        IReadOnlyDictionary<string, ProjectSemanticContext> Contexts,
        string SemanticModel,
        string RestoreStatus,
        bool TrustDiagnostics,
        bool WorkspaceHadFailures,
        IReadOnlyList<string> Messages)
    {
        public static SemanticLoadResult SyntaxOnly(string message)
        {
            return new SemanticLoadResult(new Dictionary<string, ProjectSemanticContext>(StringComparer.Ordinal), "none", "not_run", TrustDiagnostics: false, WorkspaceHadFailures: false, [message]);
        }
    }

    private sealed record RestoreResult(
        string Status,
        IReadOnlyList<string> Messages);

    private sealed record ProcessResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);

    private sealed record TypeDeclarationInfo(
        BaseTypeDeclarationSyntax Declaration,
        SourceFileContext Context,
        SemanticModel? SemanticModel,
        INamedTypeSymbol? Symbol,
        string TargetId,
        string TargetIdStability,
        string ProjectKey,
        string Name,
        string FilePath,
        int StartLine,
        int EndLine,
        int LinesOfCode,
        int NonCommentLinesOfCode,
        int MemberCount);

    private sealed record MemberDeclarationInfo(
        MemberDeclarationSyntax Declaration,
        string ParentTypeTargetId,
        SourceFileContext Context,
        SemanticModel? SemanticModel,
        ISymbol? Symbol,
        string TargetId,
        string TargetIdStability,
        string ProjectKey,
        string Name,
        string ChunkKind,
        string FilePath,
        int StartLine,
        int EndLine,
        bool HasBody,
        ControlFlowFacts ControlFlow,
        OperationFacts? Operations,
        int MethodLength,
        int ParameterCount);
}
