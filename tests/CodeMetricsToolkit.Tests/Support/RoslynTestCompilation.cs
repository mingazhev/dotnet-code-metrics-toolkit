using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Tests.Support;

internal sealed class RoslynTestCompilation
{
    private RoslynTestCompilation(CSharpCompilation compilation, SyntaxTree syntaxTree)
    {
        Compilation = compilation;
        SyntaxTree = syntaxTree;
        Root = syntaxTree.GetCompilationUnitRoot();
        SemanticModel = compilation.GetSemanticModel(syntaxTree);
    }

    public CSharpCompilation Compilation { get; }

    public SyntaxTree SyntaxTree { get; }

    public CompilationUnitSyntax Root { get; }

    public SemanticModel SemanticModel { get; }

    public static RoslynTestCompilation Create(string source)
    {
        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(
            source,
            new CSharpParseOptions(LanguageVersion.CSharp14));
        var compilation = CSharpCompilation.Create(
            "CodeMetricsToolkit.Tests.Dynamic",
            [syntaxTree],
            DefaultReferences,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return new RoslynTestCompilation(compilation, syntaxTree);
    }

    private static PortableExecutableReference[] DefaultReferences { get; } =
        CreateDefaultReferences();

    private static PortableExecutableReference[] CreateDefaultReferences()
    {
        var trustedPlatformAssemblies =
            (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
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
}
