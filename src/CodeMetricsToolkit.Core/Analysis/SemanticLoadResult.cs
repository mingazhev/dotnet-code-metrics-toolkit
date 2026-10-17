using CodeMetricsToolkit.Core.Discovery;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetricsToolkit.Core.Analysis;

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
