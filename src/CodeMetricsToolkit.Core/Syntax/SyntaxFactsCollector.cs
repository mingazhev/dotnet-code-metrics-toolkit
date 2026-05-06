using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Syntax;

public static class SyntaxFactsCollector
{
    private const string SemanticStability = "semantic";
    private const string SyntaxFallbackStability = "syntax_fallback";

    public static SyntaxAnalysisFacts Collect(
        DiscoveredSources sources,
        bool useSemantic,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var diagnostics = new List<AnalysisDiagnostic>();
        List<SourceFileContext> sourceFiles = ParseSourceFiles(sources.SourceFiles, diagnostics, cancellationToken);
        Dictionary<string, ProjectSemanticContext> semanticContexts = useSemantic
            ? CreateSemanticContexts(sourceFiles, diagnostics)
            : [];

        IReadOnlyList<FileFacts> files = sourceFiles
            .Select(context => CreateFileFacts(context))
            .OrderBy(file => file.FilePath, StringComparer.Ordinal)
            .ToArray();

        List<TypeDeclarationInfo> typeDeclarations = CollectTypeDeclarations(
            sourceFiles,
            semanticContexts,
            cancellationToken);
        Dictionary<string, TypeFacts> typeFactsById = CreateTypeFacts(typeDeclarations);
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

    private static List<SourceFileContext> ParseSourceFiles(
        IReadOnlyList<DiscoveredSourceFile> sourceFiles,
        List<AnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var contexts = new List<SourceFileContext>();

        foreach (DiscoveredSourceFile sourceFile in sourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            SourceText sourceText = SourceText.From(File.ReadAllText(sourceFile.FullPath));
            SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
                sourceText,
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12),
                path: sourceFile.FullPath,
                cancellationToken: cancellationToken);
            SyntaxNode root = syntaxTree.GetRoot(cancellationToken);
            var context = new SourceFileContext(sourceFile, sourceText, syntaxTree, root);
            contexts.Add(context);

            foreach (Diagnostic diagnostic in syntaxTree.GetDiagnostics(cancellationToken))
            {
                diagnostics.Add(ToAnalysisDiagnostic(diagnostic, sourceFile));
            }
        }

        return contexts;
    }

    private static Dictionary<string, ProjectSemanticContext> CreateSemanticContexts(
        IReadOnlyList<SourceFileContext> sourceFiles,
        List<AnalysisDiagnostic> diagnostics)
    {
        var semanticContexts = new Dictionary<string, ProjectSemanticContext>(StringComparer.Ordinal);

        foreach (IGrouping<string, SourceFileContext> projectGroup in sourceFiles.GroupBy(context => context.SourceFile.ProjectPath))
        {
            try
            {
                string assemblyName = ResolveAssemblyName(projectGroup.Key);
                CSharpCompilation compilation = CSharpCompilation.Create(
                    assemblyName,
                    projectGroup.Select(context => context.SyntaxTree),
                    CreateDefaultReferences(),
                    new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary,
                        nullableContextOptions: NullableContextOptions.Enable));

                semanticContexts.Add(projectGroup.Key, new ProjectSemanticContext(assemblyName, compilation));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                diagnostics.Add(new AnalysisDiagnostic
                {
                    Id = "semantic_model_unavailable",
                    Severity = "warning",
                    Message = exception.Message,
                    ProjectPath = projectGroup.Key,
                    FilePath = null,
                    StartLine = null,
                    EndLine = null
                });
            }
        }

        return semanticContexts;
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

            SemanticModel? semanticModel = semanticContexts.TryGetValue(context.SourceFile.ProjectPath, out ProjectSemanticContext? project)
                ? project.Compilation.GetSemanticModel(context.SyntaxTree, ignoreAccessibility: true)
                : null;
            string assemblyName = project?.AssemblyName ?? ResolveAssemblyName(context.SourceFile.ProjectPath);

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
        string fallbackName = BuildQualifiedTypeName(typeDeclaration);
        string name = symbol?.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat) ?? fallbackName;
        string? documentationCommentId = GetDocumentationCommentId(symbol);
        string targetId = documentationCommentId is null || symbol is null
            ? TargetIds.Type(context.SourceFile.ProjectKey, fallbackName, context.SourceFile.RelativePath)
            : TargetIds.TypeSemantic(symbol.ContainingAssembly.Name ?? fallbackAssemblyName, documentationCommentId);
        string targetIdStability = documentationCommentId is null ? SyntaxFallbackStability : SemanticStability;
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

    private static Dictionary<string, TypeFacts> CreateTypeFacts(IReadOnlyList<TypeDeclarationInfo> typeDeclarations)
    {
        return typeDeclarations
            .GroupBy(declaration => declaration.TargetId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    TypeDeclarationInfo first = group
                        .OrderBy(declaration => declaration.FilePath, StringComparer.Ordinal)
                        .ThenBy(declaration => declaration.StartLine)
                        .First();

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
                        LinesOfCode = group.Sum(declaration => declaration.LinesOfCode),
                        NonCommentLinesOfCode = group.Sum(declaration => declaration.NonCommentLinesOfCode),
                        MemberCount = group.Sum(declaration => declaration.MemberCount)
                    };
                },
                StringComparer.Ordinal);
    }

    private static List<MemberDeclarationInfo> CollectMemberDeclarations(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        IReadOnlyDictionary<string, TypeFacts> typeFactsById,
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
                    typeDeclaration.SemanticModel));
            }
        }

        return memberDeclarations;
    }

    private static MemberDeclarationInfo CreateMemberDeclarationInfo(
        MemberDeclarationSyntax memberDeclaration,
        TypeFacts parentType,
        SourceFileContext context,
        SemanticModel? semanticModel)
    {
        ISymbol? symbol = GetDeclaredSymbol(memberDeclaration, semanticModel);
        string memberName = GetMemberName(memberDeclaration);
        int parameterCount = GetParameterCount(memberDeclaration);
        FileLinePositionSpan lineSpan = context.SyntaxTree.GetLineSpan(memberDeclaration.Span);
        int startLine = ToOneBasedLine(lineSpan.StartLinePosition.Line);
        int endLine = ToOneBasedLine(lineSpan.EndLinePosition.Line);
        string? documentationCommentId = GetDocumentationCommentId(symbol);
        string targetId = documentationCommentId is null || symbol is null
            ? TargetIds.Member(
                context.SourceFile.ProjectKey,
                parentType.Name,
                memberName,
                parameterCount,
                context.SourceFile.RelativePath,
                startLine)
            : TargetIds.MemberSemantic(symbol.ContainingAssembly.Name, documentationCommentId);
        string targetIdStability = documentationCommentId is null ? SyntaxFallbackStability : SemanticStability;
        ControlFlowFacts controlFlowFacts = ControlFlowFactsCollector.Collect(memberDeclaration);

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
                        MethodLength = primary.MethodLength,
                        ParameterCount = primary.ParameterCount
                    };
                },
                StringComparer.Ordinal);
    }

    private static GraphEdgeFacts[] CreateGraphEdges(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        IReadOnlyList<MemberDeclarationInfo> memberDeclarations,
        IReadOnlyDictionary<string, MemberFacts> memberFactsById,
        CancellationToken cancellationToken)
    {
        var edges = new List<GraphEdgeFacts>();
        var seenEdges = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> typeTargetsBySymbol = typeDeclarations
            .Select(declaration => new
            {
                Key = GetSymbolMapKey(declaration.Symbol),
                declaration.TargetId
            })
            .Where(entry => entry.Key is not null)
            .GroupBy(entry => entry.Key!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().TargetId, StringComparer.Ordinal);
        Dictionary<string, string> memberTargetsBySymbol = memberDeclarations
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

            if (TryGetTypeTarget(typeDeclaration.Symbol.BaseType, typeTargetsBySymbol, out string? baseTypeTargetId) &&
                !IsObject(typeDeclaration.Symbol.BaseType))
            {
                AddEdge(edges, seenEdges, typeDeclaration.TargetId, baseTypeTargetId, "inherits", "exact");
            }

            foreach (INamedTypeSymbol interfaceSymbol in typeDeclaration.Symbol.Interfaces)
            {
                if (TryGetTypeTarget(interfaceSymbol, typeTargetsBySymbol, out string? interfaceTargetId))
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
            if (TryGetTypeTarget(type, typeTargetsBySymbol, out string? typeTargetId) &&
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
            if (TryGetMemberTarget(symbol, memberTargetsBySymbol, out string? targetId) && targetId != member.TargetId)
            {
                AddEdge(edges, seenEdges, member.TargetId, targetId, "calls", "exact");
            }
        }

        foreach (ObjectCreationExpressionSyntax objectCreation in memberDeclaration.Declaration.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            ISymbol? symbol = memberDeclaration.SemanticModel!.GetSymbolInfo(objectCreation, cancellationToken).Symbol;
            if (TryGetMemberTarget(symbol, memberTargetsBySymbol, out string? targetId) && targetId != member.TargetId)
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
        string key = $"{from}\n{to}\n{kind}\n{confidence}";

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

        return new FileFacts
        {
            TargetId = TargetIds.File(context.SourceFile.RelativePath),
            TargetIdStability = SyntaxFallbackStability,
            ProjectKey = context.SourceFile.ProjectKey,
            FilePath = context.SourceFile.RelativePath,
            StartLine = 1,
            EndLine = Math.Max(1, context.SourceText.Lines.Count),
            LinesOfCode = CountLines(fullSpan, context.SyntaxTree),
            NonCommentLinesOfCode = CountTokenLines(fullSpan, context.Root, context.SyntaxTree)
        };
    }

    private static PortableExecutableReference[] CreateDefaultReferences()
    {
        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

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

        string directoryName = Path.GetFileName(projectPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

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
        string name = declaration.Identifier.ValueText;

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
        HashSet<int> tokenLines = root
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

    private static AnalysisDiagnostic ToAnalysisDiagnostic(Diagnostic diagnostic, DiscoveredSourceFile sourceFile)
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
            EndLine = ToOneBasedLine(lineSpan.EndLinePosition.Line)
        };
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
        bool hasSemanticIds = typeDeclarations.Any(declaration => declaration.TargetIdStability == SemanticStability) ||
            memberDeclarations.Any(declaration => declaration.TargetIdStability == SemanticStability);
        bool hasFallbackIds = typeDeclarations.Any(declaration => declaration.TargetIdStability != SemanticStability) ||
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
        string? documentationCommentId = GetDocumentationCommentId(symbol);

        return documentationCommentId is null || symbol is null
            ? null
            : $"{symbol.OriginalDefinition.ContainingAssembly.Name}/{documentationCommentId}";
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

        string? key = GetSymbolMapKey(namedType.OriginalDefinition);

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

        string? key = GetSymbolMapKey(symbol.OriginalDefinition);

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
        CSharpCompilation Compilation);

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
        int MethodLength,
        int ParameterCount);
}
