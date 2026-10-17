using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class SyntaxFactsCollectorTests
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

        SyntaxAnalysisFacts facts = await SyntaxFactsCollector.CollectAsync(
            sources,
            useSemantic: false,
            noRestore: true,
            CancellationToken.None);

        FileFacts file = Assert.Single(facts.Files);
        TypeFacts type = Assert.Single(facts.Types);
        Assert.Equal(11, file.NonCommentLinesOfCode);
        Assert.Equal(0, file.BlankLineCount);
        Assert.Equal(11, type.NonCommentLinesOfCode);
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
