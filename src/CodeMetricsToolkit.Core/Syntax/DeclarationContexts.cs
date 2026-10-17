using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Syntax;

internal sealed record SourceFileContext(
    DiscoveredSourceFile SourceFile,
    SourceText SourceText,
    SyntaxTree SyntaxTree,
    SyntaxNode Root);

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
    int TokenLineCount);

internal sealed record MemberDeclarationInfo(
    MemberDeclarationSyntax Declaration,
    string ParentTypeTargetId,
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
    int MemberLength,
    int ParameterCount);
