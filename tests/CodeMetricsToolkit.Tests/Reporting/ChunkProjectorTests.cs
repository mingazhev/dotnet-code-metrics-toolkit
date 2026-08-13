using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Syntax;
using CodeMetricsToolkit.Tests.Support;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class ChunkProjectorTests
{
    [Fact]
    public async Task ProjectUsesCollectedSourceSnapshotAfterLiveFileChanges()
    {
        using var directory = new TemporaryDirectory();
        var sourcePath = Path.Combine(directory.Path, "Snapshot.cs");
        const string original =
            "namespace Sample;\ninternal sealed class Snapshot\n{\n    public string Value() => \"original\";\n}\n";
        File.WriteAllText(sourcePath, original);

        DiscoveredSources sources = SourceFileDiscovery.Discover(
            directory.Path,
            includeGeneratedCode: false);
        SyntaxAnalysisFacts facts = await SyntaxFactsCollector.CollectAsync(
            sources,
            useSemantic: false,
            noRestore: true,
            CancellationToken.None);

        File.WriteAllText(sourcePath, original.Replace("original", "mutated", StringComparison.Ordinal));

        IReadOnlyList<ChunkLine> chunks = ChunkProjector.Project(facts, includeText: true);

        Assert.Contains(chunks, chunk => chunk.Text?.Contains("original", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(chunks, chunk => chunk.Text?.Contains("mutated", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ProjectCreatesManyChunksWithoutReadingTheFilesystem()
    {
        const string path = "Many.cs";
        const string source = "internal sealed class Many\n{\n    public void Run() { }\n}";
        FileFacts file = TestFacts.File("file:many", path, lines: 4);
        TypeFacts type = TestFacts.Type(
            "type:many",
            file.TargetId,
            path,
            startLine: 1,
            endLine: 4,
            memberCount: 100);
        MemberFacts[] members = Enumerable.Range(0, 100)
            .Select(index => TestFacts.Member(
                $"member:{index}",
                type.TargetId,
                path,
                startLine: 3,
                endLine: 3))
            .ToArray();
        SyntaxAnalysisFacts facts = TestFacts.Analysis(
            files: [file],
            types: [type],
            members: members,
            rootPath: Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}"),
            sourceTextSnapshots: new Dictionary<string, SourceText>(StringComparer.Ordinal)
            {
                [path] = SourceText.From(source)
            });

        IReadOnlyList<ChunkLine> chunks = ChunkProjector.Project(facts, includeText: true);

        Assert.Equal(102, chunks.Count);
        Assert.All(chunks, chunk => Assert.NotNull(chunk.Text));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeMetricsToolkit-chunks-{Guid.NewGuid():N}");
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
