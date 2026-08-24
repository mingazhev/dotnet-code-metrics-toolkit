using System.Diagnostics.CodeAnalysis;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Core.Validation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Syntax;

public static class SyntaxFactsCollector
{
    private const string SemanticStability = "semantic";
    private const string SyntaxFallbackStability = "syntax_fallback";
    private const string LineFallbackStability = "line_fallback";

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
            ? await SemanticWorkspaceLoader.LoadAsync(sources, noRestore, diagnostics, diagnosticKeys, cancellationToken)
                .ConfigureAwait(false)
            : SemanticLoadResult.SyntaxOnly(
                semanticInitializationFailure ?? "Syntax-only analysis was requested.");
        IReadOnlyList<DiscoveredSourceFile> analysisSourceFiles = semanticLoad.Contexts.Count > 0 &&
            !semanticLoad.WorkspaceHadFailures
            ? semanticLoad.SourceFiles
            : sources.SourceFiles;
        List<SourceFileContext> sourceFiles = await ParseSourceFilesAsync(
                analysisSourceFiles,
                semanticLoad.Contexts,
                diagnostics,
                diagnosticKeys,
                cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<FileFacts> files = sourceFiles
            .Select(context => CreateFileFacts(context))
            .GroupBy(file => file.FilePath, StringComparer.Ordinal)
            .Select(group => group.First())
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
            Mode = DetermineMode(useSemantic, semanticLoad, typeDeclarations, memberDeclarations),
            Health = SemanticWorkspaceLoader.CreateHealth(useSemantic, semanticLoad, semanticInitializationFailure),
            RootPath = sources.RootPath,
            ProjectPaths = sources.ProjectPaths,
            Files = files,
            Types = types,
            Members = members,
            GraphEdges = graphEdges,
            ProjectFileMemberships = analysisSourceFiles
                .Select(source => new ProjectFileMembershipFacts(
                    source.ProjectKey,
                    source.RelativePath))
                .Distinct()
                .OrderBy(membership => membership.ProjectKey, StringComparer.Ordinal)
                .ThenBy(membership => membership.FilePath, StringComparer.Ordinal)
                .ToArray(),
            SourceTextSnapshots = sourceFiles
                .GroupBy(context => context.SourceFile.RelativePath, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().SourceText.ToString(),
                    StringComparer.Ordinal),
            Diagnostics = diagnostics
                .OrderBy(diagnostic => diagnostic.FilePath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.StartLine ?? int.MaxValue)
                .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.ProjectPath ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(diagnostic => diagnostic.Message, StringComparer.Ordinal)
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
                sourceText = SourceText.From(
                    await FileKind.ReadAllTextAsync(
                            sourceFile.FullPath,
                            InputIsolator.MaxIsolatedFileBytes,
                            cancellationToken)
                        .ConfigureAwait(false));
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
                AnalysisDiagnosticCollector.Add(
                    diagnostics,
                    diagnosticKeys,
                    AnalysisDiagnosticCollector.FromSyntax(diagnostic, sourceFile));
            }
        }

        return contexts;
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
        var targetIdStability = documentationCommentId is null ? LineFallbackStability : SemanticStability;
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
            if (edges.Count >= ValidationInputLimits.DefaultMaxNdjsonRecords)
            {
                throw new InvalidDataException(
                    $"Analysis produced more than {ValidationInputLimits.DefaultMaxNdjsonRecords} graph edges.");
            }

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

    private static string NormalizeFullPath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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

    private static string DetermineMode(
        bool useSemantic,
        SemanticLoadResult semanticLoad,
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        IReadOnlyList<MemberDeclarationInfo> memberDeclarations)
    {
        if (!useSemantic)
        {
            return "syntax";
        }

        var hasSemanticIds = typeDeclarations.Any(declaration => declaration.TargetIdStability == SemanticStability) ||
            memberDeclarations.Any(declaration => declaration.TargetIdStability == SemanticStability);
        var hasFallbackIds = typeDeclarations.Any(declaration => declaration.TargetIdStability != SemanticStability) ||
            memberDeclarations.Any(declaration => declaration.TargetIdStability != SemanticStability);
        var semanticPipelineRan = string.Equals(semanticLoad.SemanticModel, "msbuild", StringComparison.Ordinal);

        if (hasSemanticIds && hasFallbackIds)
        {
            return "partial_semantic";
        }

        if (hasSemanticIds || (semanticPipelineRan && !hasFallbackIds))
        {
            return "semantic";
        }

        if (semanticPipelineRan)
        {
            return "partial_semantic";
        }

        return "syntax";
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

}
