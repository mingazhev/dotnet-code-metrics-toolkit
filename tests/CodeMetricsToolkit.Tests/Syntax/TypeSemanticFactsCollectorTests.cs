using CodeMetricsToolkit.Core.Syntax;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Tests.Support;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class TypeSemanticFactsCollectorTests
{
    [Theory]
    [InlineData("class Sample { }", 0)]
    [InlineData("class Sample : System.Exception { }", 1)]
    [InlineData("record Sample;", 0)]
    [InlineData("interface Sample { }", 0)]
    [InlineData("struct Sample { }", 0)]
    [InlineData("readonly record struct Sample(int Value);", 0)]
    [InlineData("enum Sample { Value }", 0)]
    public void InheritanceDepthCountsOnlyClassBaseTypes(string source, int expected)
    {
        var context = RoslynTestCompilation.Create(source);
        BaseTypeDeclarationSyntax declaration = context.Root.DescendantNodes()
            .OfType<BaseTypeDeclarationSyntax>().Single();
        var symbol = (INamedTypeSymbol)context.SemanticModel.GetDeclaredSymbol(declaration)!;

        TypeSemanticFacts? facts = TypeSemanticFactsCollector.Collect(
            [new SemanticTypeDeclaration(declaration, context.SemanticModel, symbol)],
            CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(expected, facts.InheritanceDepth);
    }

    [Fact]
    public void CollectCountsDistinctArgumentsOfRepeatedGenericDefinitions()
    {
        var context = RoslynTestCompilation.Create(
            """
            using Coupled = Pair<Box<First[]>, Box<Second>>;
            public class First { }
            public class Second { }
            public class Box<T> { }
            public class Pair<T, U> { }
            public sealed class Sample
            {
                public Coupled Transform(Coupled value) => value;
            }
            """);
        ClassDeclarationSyntax declaration = context.Root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "Sample");
        var symbol = (INamedTypeSymbol)context.SemanticModel.GetDeclaredSymbol(declaration)!;

        TypeSemanticFacts? facts = TypeSemanticFactsCollector.Collect(
            [new SemanticTypeDeclaration(declaration, context.SemanticModel, symbol)],
            CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(4, facts.ClassCoupling);
    }

    [Fact]
    public void CollectComputesInheritanceCouplingAndDocumentedPublicApiExactly()
    {
        var context = RoslynTestCompilation.Create(
            """
            public class Root { }
            public class Base : Root { }
            public class Other { }

            /// <summary>Documented type.</summary>
            public sealed class Sample : Base
            {
                /// <summary>Documented operation.</summary>
                public Other Transform(Other input) => input;

                public void Undocumented() { }
            }
            """);
        ClassDeclarationSyntax declaration = context.Root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(type => type.Identifier.ValueText == "Sample");
        var symbol = (INamedTypeSymbol)context.SemanticModel.GetDeclaredSymbol(declaration)!;

        TypeSemanticFacts? facts = TypeSemanticFactsCollector.Collect(
            [new SemanticTypeDeclaration(declaration, context.SemanticModel, symbol)],
            CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(2, facts.InheritanceDepth);
        Assert.Equal(2, facts.ClassCoupling);
        Assert.Equal(3, facts.PublicApiCount);
        Assert.Equal(2, facts.DocumentedPublicApiCount);
    }

    [Fact]
    public void CollectDoesNotExposeMembersOfAnInternalContainingTypeAsPublicApi()
    {
        var context = RoslynTestCompilation.Create(
            """
            internal sealed class Hidden
            {
                /// <summary>Still inaccessible.</summary>
                public void Method() { }
            }
            """);
        ClassDeclarationSyntax declaration = context.Root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single();
        var symbol = (INamedTypeSymbol)context.SemanticModel.GetDeclaredSymbol(declaration)!;

        TypeSemanticFacts? facts = TypeSemanticFactsCollector.Collect(
            [new SemanticTypeDeclaration(declaration, context.SemanticModel, symbol)],
            CancellationToken.None);

        Assert.NotNull(facts);
        Assert.Equal(0, facts.PublicApiCount);
        Assert.Equal(0, facts.DocumentedPublicApiCount);
    }
}
