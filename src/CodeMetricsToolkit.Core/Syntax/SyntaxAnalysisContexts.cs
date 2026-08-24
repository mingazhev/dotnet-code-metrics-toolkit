using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Syntax;

internal sealed record SourceFileContext(
    DiscoveredSourceFile SourceFile,
    SourceText SourceText,
    SyntaxTree SyntaxTree,
    SyntaxNode Root);

internal sealed record ProjectSemanticContext(
    string AssemblyName,
    CSharpCompilation Compilation,
    IReadOnlyDictionary<string, SyntaxTree> SyntaxTreesByFullPath);

internal sealed record SemanticLoadResult(
    IReadOnlyDictionary<string, ProjectSemanticContext> Contexts,
    IReadOnlyList<DiscoveredSourceFile> SourceFiles,
    string SemanticModel,
    string RestoreStatus,
    bool TrustDiagnostics,
    bool WorkspaceHadFailures,
    IReadOnlyList<string> Messages)
{
    public static SemanticLoadResult SyntaxOnly(string message)
    {
        return new SemanticLoadResult(
            new Dictionary<string, ProjectSemanticContext>(StringComparer.Ordinal),
            [],
            "none",
            "not_run",
            TrustDiagnostics: false,
            WorkspaceHadFailures: false,
            [message]);
    }
}

internal sealed record TypeDeclarationInfo(
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

internal sealed record MemberDeclarationInfo(
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
