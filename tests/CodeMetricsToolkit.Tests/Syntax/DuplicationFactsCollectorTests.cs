using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class DuplicationFactsCollectorTests
{
    [Fact]
    public void CollectFindsCrossFileClonesDespiteTriviaDifferences()
    {
        var plain = Declarations(25);
        var commented = Declarations(25, comments: true, extraWhitespace: true);

        DuplicationFacts facts = DuplicationFactsCollector.Collect(
            [Parse("z.cs", commented), Parse("a.cs", plain)],
            CancellationToken.None);

        Assert.Equal(["a.cs", "z.cs"], facts.Files.Select(file => file.FilePath));
        Assert.All(facts.Files, file =>
        {
            DuplicationRegionFacts region = Assert.Single(file.Regions);
            Assert.Equal(1, region.StartLine);
            Assert.Equal(25, region.EndLine);
            Assert.Equal(25, file.DuplicatedLineCount);
        });
        Assert.Equal(2, facts.Files.SelectMany(file => file.Regions).Select(region => region.Tag).Distinct().Count());
    }

    [Fact]
    public void CollectMergesOverlappingWindowsIntoTwoSameFileRegions()
    {
        var copy = Declarations(25);

        DuplicationFacts facts = DuplicationFactsCollector.Collect(
            [Parse("same.cs", copy + "\n" + copy)],
            CancellationToken.None);

        DuplicationFileFacts file = Assert.Single(facts.Files);
        Assert.Equal(2, file.Regions.Count);
        Assert.Equal((1, 25), (file.Regions[0].StartLine, file.Regions[0].EndLine));
        Assert.Equal((26, 50), (file.Regions[1].StartLine, file.Regions[1].EndLine));
        Assert.Equal(50, file.DuplicatedLineCount);
    }

    [Fact]
    public void CollectDoesNotCountOverlappingSameFileOccurrencesAsAClone()
    {
        var cycle = Declarations(10);
        var source = string.Join("\n", [cycle, cycle, cycle]);

        DuplicationFacts facts = DuplicationFactsCollector.Collect(
            [Parse("overlap.cs", source)],
            CancellationToken.None);

        Assert.Equal(0, Assert.Single(facts.Files).DuplicatedLineCount);
        Assert.Empty(Assert.Single(facts.Files).Regions);
    }

    [Fact]
    public void CollectRequiresAtLeastOneHundredConsecutiveTokens()
    {
        var ninetyNineTokens = string.Join("\n", Declarations(19).Split('\n').Append("v019 = 19;"));

        DuplicationFacts facts = DuplicationFactsCollector.Collect(
            [Parse("a.cs", ninetyNineTokens), Parse("b.cs", ninetyNineTokens)],
            CancellationToken.None);

        Assert.All(facts.Files, file =>
        {
            Assert.Empty(file.Regions);
            Assert.Equal(0, file.DuplicatedLineCount);
        });
    }

    [Fact]
    public void CollectAcceptsExactlyOneHundredConsecutiveTokens()
    {
        var exactlyOneHundredTokens = Declarations(20);
        SourceFileContext first = Parse("a.cs", exactlyOneHundredTokens);
        SourceFileContext second = Parse("b.cs", exactlyOneHundredTokens);
        Assert.Equal(100, first.Root.DescendantTokens().Count(token =>
            !token.IsMissing && !token.IsKind(SyntaxKind.EndOfFileToken) && !token.Span.IsEmpty));

        DuplicationFacts facts = DuplicationFactsCollector.Collect(
            [first, second],
            CancellationToken.None);

        Assert.All(facts.Files, file =>
        {
            DuplicationRegionFacts region = Assert.Single(file.Regions);
            Assert.Equal((1, 20), (region.StartLine, region.EndLine));
            Assert.Equal(20, file.DuplicatedLineCount);
        });
    }

    [Fact]
    public void CollectRequiresTenPhysicalLinesAtEachOccurrence()
    {
        var compact = Declarations(20, statementsPerLine: 4);

        DuplicationFacts facts = DuplicationFactsCollector.Collect(
            [Parse("a.cs", compact), Parse("b.cs", compact)],
            CancellationToken.None);

        Assert.All(facts.Files, file => Assert.Empty(file.Regions));
    }

    [Theory]
    [InlineData("identifier")]
    [InlineData("literal")]
    public void CollectKeepsIdentifiersAndLiteralsInTheTokenIdentity(string changedToken)
    {
        var original = Declarations(20);
        var changed = Declarations(20, changedAt: 10, changedToken: changedToken);

        DuplicationFacts facts = DuplicationFactsCollector.Collect(
            [Parse("a.cs", original), Parse("b.cs", changed)],
            CancellationToken.None);

        Assert.All(facts.Files, file => Assert.Empty(file.Regions));
    }

    [Fact]
    public void CollectAssignsDeterministicFileAndRegionOrder()
    {
        SourceFileContext[] contexts = [
            Parse("z.cs", Declarations(25)),
            Parse("a.cs", Declarations(25)),
            Parse("m.cs", Declarations(25))
        ];

        DuplicationFacts first = DuplicationFactsCollector.Collect(contexts, CancellationToken.None);
        DuplicationFacts second = DuplicationFactsCollector.Collect(contexts.Reverse().ToArray(), CancellationToken.None);

        Assert.Equal(
            first.Files.SelectMany(file => file.Regions.Select(region => (file.FilePath, region.StartLine, region.EndLine, region.Tag))),
            second.Files.SelectMany(file => file.Regions.Select(region => (file.FilePath, region.StartLine, region.EndLine, region.Tag))));
        Assert.Equal(["a.cs", "m.cs", "z.cs"], first.Files.Select(file => file.FilePath));
    }

    [Fact]
    public void CollectObservesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            DuplicationFactsCollector.Collect(
                [Parse("a.cs", Declarations(25))],
                cancellation.Token));
    }

    [Fact]
    public void CollectFailsClosedWhenTokenLimitIsExceeded()
    {
        var limits = new DuplicationCollectorLimits(MaxTokens: 99, MaxWindows: 500, MaxWorkUnits: 10_000);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            DuplicationFactsCollector.Collect(
                [Parse("a.cs", Declarations(20))],
                CancellationToken.None,
                limits));

        Assert.Contains("99", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectFailsClosedWhenWindowLimitIsExceeded()
    {
        var limits = new DuplicationCollectorLimits(MaxTokens: 500, MaxWindows: 0, MaxWorkUnits: 10_000);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            DuplicationFactsCollector.Collect(
                [Parse("a.cs", Declarations(25))],
                CancellationToken.None,
                limits));

        Assert.Contains("window limit", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CollectFailsClosedWhenWorkLimitIsExceeded()
    {
        var limits = new DuplicationCollectorLimits(MaxTokens: 500, MaxWindows: 500, MaxWorkUnits: 1);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            DuplicationFactsCollector.Collect(
                [Parse("a.cs", Declarations(25))],
                CancellationToken.None,
                limits));

        Assert.Contains("work limit", exception.Message, StringComparison.Ordinal);
    }

    private static string Declarations(
        int count,
        bool comments = false,
        bool extraWhitespace = false,
        int statementsPerLine = 1,
        int changedAt = -1,
        string? changedToken = null)
    {
        var statements = Enumerable.Range(0, count)
            .Select(index =>
            {
                var name = index == changedAt && changedToken == "identifier"
                    ? $"changed{index:D3}"
                    : $"v{index:D3}";
                var value = index == changedAt && changedToken == "literal" ? index + 1000 : index;
                var statement = extraWhitespace
                    ? $"int   {name}\t =   {value} ;"
                    : $"int {name} = {value};";

                return comments ? statement + " // trivia" : statement;
            })
            .ToArray();

        return string.Join(
            "\n",
            statements.Chunk(statementsPerLine).Select(line => string.Join(" ", line)));
    }

    private static SourceFileContext Parse(string relativePath, string source)
    {
        var fullPath = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "duplication-tests", relativePath));
        var text = SourceText.From(source);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(
            text,
            new CSharpParseOptions(LanguageVersion.CSharp14),
            fullPath);

        return new SourceFileContext(
            new DiscoveredSourceFile(fullPath, relativePath, "Sample.csproj", "sample-project"),
            text,
            tree,
            tree.GetRoot());
    }
}
