using System.Diagnostics.CodeAnalysis;
using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Core.Syntax;

internal static class SemanticGraphCollector
{
    public static GraphEdgeFacts[] CreateGraphEdges(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        IReadOnlyList<MemberDeclarationInfo> memberDeclarations,
        IReadOnlyDictionary<string, MemberFacts> memberFactsById,
        IReadOnlyList<NamespaceFacts> namespaces,
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

            if (typeDeclaration.Symbol is not null)
            {
                if (TryGetTypeTarget(typeDeclaration.Symbol.BaseType, typeTargetsBySymbol, out var baseTypeTargetId) &&
                    typeDeclaration.Symbol.BaseType?.SpecialType != SpecialType.System_Object)
                {
                    AddEdge(edges, seenEdges, typeDeclaration.TargetId, baseTypeTargetId, "inherits", "exact");
                }

                foreach (INamedTypeSymbol interfaceSymbol in typeDeclaration.Symbol.Interfaces)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (TryGetTypeTarget(interfaceSymbol, typeTargetsBySymbol, out var interfaceTargetId))
                    {
                        AddEdge(edges, seenEdges, typeDeclaration.TargetId, interfaceTargetId, "implements", "exact");
                    }
                }

                AddTypeUsageEdges(typeDeclaration, typeTargetsBySymbol, edges, seenEdges, cancellationToken);
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

        AddNamespaceDependencyEdges(
            typeDeclarations,
            memberFactsById,
            namespaces,
            edges,
            seenEdges,
            cancellationToken);

        return edges
            .OrderBy(edge => edge.From, StringComparer.Ordinal)
            .ThenBy(edge => edge.Kind, StringComparer.Ordinal)
            .ThenBy(edge => edge.To, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AddNamespaceDependencyEdges(
        IReadOnlyList<TypeDeclarationInfo> typeDeclarations,
        IReadOnlyDictionary<string, MemberFacts> memberFactsById,
        IReadOnlyList<NamespaceFacts> namespaces,
        List<GraphEdgeFacts> edges,
        HashSet<string> seenEdges,
        CancellationToken cancellationToken)
    {
        var typeTargetIds = typeDeclarations
            .Select(declaration => declaration.TargetId)
            .ToHashSet(StringComparer.Ordinal);
        var namespaceByTypeId = namespaces
            .SelectMany(namespaceFacts => namespaceFacts.TypeTargetIds.Select(typeTargetId =>
                new KeyValuePair<string, string>(typeTargetId, namespaceFacts.TargetId)))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);

        var existingEdgeCount = edges.Count;
        for (var edgeIndex = 0; edgeIndex < existingEdgeCount; edgeIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GraphEdgeFacts edge = edges[edgeIndex];
            if (edge.Kind is not ("inherits" or "implements" or "uses_type"))
            {
                continue;
            }

            var sourceTypeId = typeTargetIds.Contains(edge.From)
                ? edge.From
                : memberFactsById.TryGetValue(edge.From, out MemberFacts? member)
                    ? member.ParentTypeTargetId
                    : null;
            if (sourceTypeId is null ||
                !namespaceByTypeId.TryGetValue(sourceTypeId, out var sourceNamespaceId) ||
                !namespaceByTypeId.TryGetValue(edge.To, out var targetNamespaceId) ||
                string.Equals(sourceNamespaceId, targetNamespaceId, StringComparison.Ordinal))
            {
                continue;
            }

            AddEdge(edges, seenEdges, sourceNamespaceId, targetNamespaceId, "depends_on", "exact");
        }
    }

    private static void AddTypeUsageEdges(
        TypeDeclarationInfo typeDeclaration,
        IReadOnlyDictionary<string, string> typeTargetsBySymbol,
        List<GraphEdgeFacts> edges,
        HashSet<string> seenEdges,
        CancellationToken cancellationToken)
    {
        SemanticModel semanticModel = typeDeclaration.SemanticModel!;
        BaseTypeDeclarationSyntax declaration = typeDeclaration.Declaration;

        foreach (SyntaxNode node in declaration.DescendantNodes(descendIntoChildren: node =>
                     ReferenceEquals(node, declaration) || node is not BaseTypeDeclarationSyntax))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (node is TypeSyntax typeSyntax)
            {
                ITypeSymbol? usedType = semanticModel.GetTypeInfo(typeSyntax, cancellationToken).Type;
                if (TryGetTypeTarget(usedType, typeTargetsBySymbol, out var typeTargetId) &&
                    typeTargetId != typeDeclaration.TargetId)
                {
                    AddEdge(edges, seenEdges, typeDeclaration.TargetId, typeTargetId, "uses_type", "exact");
                }
            }

            if (node is IdentifierNameSyntax or MemberAccessExpressionSyntax)
            {
                ISymbol? symbol = semanticModel.GetSymbolInfo(node, cancellationToken).Symbol;
                INamedTypeSymbol? declaringType = GetStaticMemberDeclaringType(symbol);
                if (TryGetTypeTarget(declaringType, typeTargetsBySymbol, out var typeTargetId) &&
                    typeTargetId != typeDeclaration.TargetId)
                {
                    AddEdge(edges, seenEdges, typeDeclaration.TargetId, typeTargetId, "uses_type", "exact");
                }
            }
        }
    }

    private static INamedTypeSymbol? GetStaticMemberDeclaringType(ISymbol? symbol)
    {
        return symbol switch
        {
            IFieldSymbol { IsStatic: true } field => field.ContainingType,
            IPropertySymbol { IsStatic: true } property => property.ContainingType,
            IMethodSymbol { IsStatic: true } method => method.ContainingType,
            _ => null
        };
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
            if (edges.Count >= ArtifactLimits.MaxRecords)
            {
                throw new InvalidDataException(
                    $"Analysis produced more than {ArtifactLimits.MaxRecords} graph edges.");
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

    private static string? GetSymbolMapKey(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return null;
        }

        var documentationCommentId = symbol.OriginalDefinition.GetDocumentationCommentId();
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
}
