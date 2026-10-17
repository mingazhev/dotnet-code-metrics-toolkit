using CodeMetricsToolkit.Core.Analysis;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Tests.Analysis;

public sealed class AnalysisFactsCollectorTests
{
    [Fact]
    public async Task CollectCountsMultilineTokensForFileAndTypeFacts()
    {
        using var directory = new TemporaryDirectory();
        File.WriteAllText(Path.Combine(directory.Path, "Sample.cs"), string.Join(
            '\n',
            "class Sample",
            "{",
            "    string Raw = \"\"\"",
            "        raw first",
            "",
            "        raw last",
            "        \"\"\";",
            "    string Verbatim = @\"verbatim first",
            "",
            "verbatim last\";",
            "}"));
        DiscoveredSources sources = SourceFileDiscovery.Discover(
            directory.Path,
            includeGeneratedCode: false);

        AnalysisFacts facts = await AnalysisFactsCollector.CollectAsync(
            sources,
            useSemantic: false,
            noRestore: true,
            CancellationToken.None);

        FileFacts file = Assert.Single(facts.Files);
        TypeFacts type = Assert.Single(facts.Types);
        Assert.Equal(11, file.TokenLineCount);
        Assert.Equal(0, file.BlankLineCount);
        Assert.Equal(11, type.TokenLineCount);
    }

    [Fact]
    public async Task CollectAttachesDuplicationFactsForParsedPhysicalFiles()
    {
        using var directory = new TemporaryDirectory();
        var source = string.Join(
            '\n',
            Enumerable.Range(0, 25).Select(index => $"int v{index:D3} = {index};"));
        File.WriteAllText(Path.Combine(directory.Path, "A.cs"), source);
        File.WriteAllText(Path.Combine(directory.Path, "B.cs"), source);
        DiscoveredSources sources = SourceFileDiscovery.Discover(
            directory.Path,
            includeGeneratedCode: false);

        AnalysisFacts facts = await AnalysisFactsCollector.CollectAsync(
            sources,
            useSemantic: false,
            noRestore: true,
            CancellationToken.None);

        Assert.NotNull(facts.Duplication);
        Assert.Equal(2, facts.Duplication.Files.Count);
        Assert.All(facts.Duplication.Files, file =>
        {
            Assert.Equal(25, file.DuplicatedLineCount);
            Assert.Single(file.Regions);
        });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeMetricsToolkit-syntax-facts-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
