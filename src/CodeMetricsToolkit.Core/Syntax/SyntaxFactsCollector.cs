using System.Globalization;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Syntax;

public static class SyntaxFactsCollector
{
    public static SyntaxAnalysisFacts Collect(DiscoveredSources sources, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sources);

        var files = new List<FileFacts>();
        var types = new List<TypeFacts>();
        var members = new List<MemberFacts>();
        var diagnostics = new List<AnalysisDiagnostic>();

        foreach (DiscoveredSourceFile sourceFile in sources.SourceFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            CollectFileFacts(sourceFile, files, types, members, diagnostics, cancellationToken);
        }

        return new SyntaxAnalysisFacts
        {
            RootPath = sources.RootPath,
            ProjectPaths = sources.ProjectPaths,
            Files = files,
            Types = types,
            Members = members,
            Diagnostics = diagnostics
        };
    }

    private static void CollectFileFacts(
        DiscoveredSourceFile sourceFile,
        List<FileFacts> files,
        List<TypeFacts> types,
        List<MemberFacts> members,
        List<AnalysisDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        SourceText sourceText = SourceText.From(File.ReadAllText(sourceFile.FullPath));
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            sourceText,
            path: sourceFile.FullPath,
            cancellationToken: cancellationToken);
        SyntaxNode root = syntaxTree.GetRoot(cancellationToken);
        var fullSpan = new TextSpan(0, sourceText.Length);

        files.Add(new FileFacts
        {
            TargetId = TargetIds.File(sourceFile.RelativePath),
            ProjectKey = sourceFile.ProjectKey,
            FilePath = sourceFile.RelativePath,
            StartLine = 1,
            EndLine = Math.Max(1, sourceText.Lines.Count),
            LinesOfCode = CountLines(fullSpan, syntaxTree),
            NonCommentLinesOfCode = CountTokenLines(fullSpan, root, syntaxTree)
        });

        foreach (Diagnostic diagnostic in syntaxTree.GetDiagnostics(cancellationToken))
        {
            diagnostics.Add(ToAnalysisDiagnostic(diagnostic, sourceFile));
        }

        foreach (BaseTypeDeclarationSyntax typeDeclaration in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
        {
            TypeFacts typeFacts = CreateTypeFacts(typeDeclaration, sourceFile, root, syntaxTree);
            types.Add(typeFacts);

            foreach (MemberDeclarationSyntax memberDeclaration in GetSupportedMembers(typeDeclaration))
            {
                members.Add(CreateMemberFacts(memberDeclaration, typeFacts, sourceFile, syntaxTree));
            }
        }
    }

    private static TypeFacts CreateTypeFacts(
        BaseTypeDeclarationSyntax typeDeclaration,
        DiscoveredSourceFile sourceFile,
        SyntaxNode root,
        SyntaxTree syntaxTree)
    {
        FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(typeDeclaration.Span);
        string typeName = BuildQualifiedTypeName(typeDeclaration);
        TextSpan span = typeDeclaration.Span;

        return new TypeFacts
        {
            TargetId = TargetIds.Type(sourceFile.ProjectKey, typeName, sourceFile.RelativePath),
            ParentFileTargetId = TargetIds.File(sourceFile.RelativePath),
            ProjectKey = sourceFile.ProjectKey,
            Name = typeName,
            FilePath = sourceFile.RelativePath,
            StartLine = ToOneBasedLine(lineSpan.StartLinePosition.Line),
            EndLine = ToOneBasedLine(lineSpan.EndLinePosition.Line),
            LinesOfCode = CountLines(span, syntaxTree),
            NonCommentLinesOfCode = CountTokenLines(span, root, syntaxTree),
            MemberCount = GetSupportedMembers(typeDeclaration).Count()
        };
    }

    private static MemberFacts CreateMemberFacts(
        MemberDeclarationSyntax memberDeclaration,
        TypeFacts parentType,
        DiscoveredSourceFile sourceFile,
        SyntaxTree syntaxTree)
    {
        FileLinePositionSpan lineSpan = syntaxTree.GetLineSpan(memberDeclaration.Span);
        int startLine = ToOneBasedLine(lineSpan.StartLinePosition.Line);
        int endLine = ToOneBasedLine(lineSpan.EndLinePosition.Line);
        string memberName = GetMemberName(memberDeclaration);
        int parameterCount = GetParameterCount(memberDeclaration);

        return new MemberFacts
        {
            TargetId = TargetIds.Member(
                sourceFile.ProjectKey,
                parentType.Name,
                memberName,
                parameterCount,
                sourceFile.RelativePath,
                startLine),
            ParentTypeTargetId = parentType.TargetId,
            ProjectKey = sourceFile.ProjectKey,
            Name = $"{parentType.Name}.{memberName}",
            FilePath = sourceFile.RelativePath,
            StartLine = startLine,
            EndLine = endLine,
            MethodLength = Math.Max(1, endLine - startLine + 1),
            ParameterCount = parameterCount
        };
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

    private static int GetParameterCount(MemberDeclarationSyntax memberDeclaration)
    {
        return memberDeclaration switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList.Parameters.Count,
            IndexerDeclarationSyntax indexer => indexer.ParameterList.Parameters.Count,
            _ => 0
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
}
