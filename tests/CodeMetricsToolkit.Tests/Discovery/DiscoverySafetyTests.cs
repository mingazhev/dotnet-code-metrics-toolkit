using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Tests.Discovery;

public sealed class DiscoverySafetyTests
{
    [Fact]
    public void DefaultIsolationLimitsMatchDocumentedSecurityBoundary()
    {
        Assert.Equal(4L * 1024 * 1024 * 1024, InputIsolationLimits.Default.MaxTotalBytes);
        Assert.Equal(256L * 1024 * 1024, InputIsolationLimits.Default.MaxFileBytes);
        Assert.Equal(250_000, InputIsolationLimits.Default.MaxFileCount);
        Assert.Equal(50_000, InputIsolationLimits.Default.MaxDirectoryCount);
        Assert.Equal(96, InputIsolationLimits.Default.MaxDepth);
    }

    [Fact]
    public void DiscoveryDoesNotFollowDirectoryOrFileSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryDirectory();
        var input = fixture.CreateDirectory("input");
        var outside = fixture.CreateDirectory("outside");
        File.WriteAllText(Path.Combine(input, "Included.cs"), "internal sealed class Included { }");
        File.WriteAllText(Path.Combine(outside, "Escaped.cs"), "internal sealed class Escaped { }");
        Directory.CreateSymbolicLink(Path.Combine(input, "linked-directory"), outside);
        File.CreateSymbolicLink(
            Path.Combine(input, "Linked.cs"),
            Path.Combine(outside, "Escaped.cs"));

        DiscoveredSources discovered = SourceFileDiscovery.Discover(input, includeGeneratedCode: false);

        Assert.Collection(
            discovered.SourceFiles,
            source => Assert.Equal("Included.cs", source.RelativePath));
    }

    [Fact]
    public void IsolatedCopyDoesNotFollowSymlinks()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryDirectory();
        var input = fixture.CreateDirectory("input");
        var outside = fixture.CreateDirectory("outside");
        File.WriteAllText(Path.Combine(input, "Included.cs"), "internal sealed class Included { }");
        File.WriteAllText(Path.Combine(outside, "Secret.cs"), "internal sealed class Secret { }");
        Directory.CreateSymbolicLink(Path.Combine(input, "linked-directory"), outside);
        File.CreateSymbolicLink(
            Path.Combine(input, "Linked.cs"),
            Path.Combine(outside, "Secret.cs"));

        using IsolatedInput isolated = InputIsolator.CopyToTemporaryDirectory(
            input,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(isolated.IsolatedRootPath, "Included.cs")));
        Assert.False(Directory.Exists(Path.Combine(isolated.IsolatedRootPath, "linked-directory")));
        Assert.False(File.Exists(Path.Combine(isolated.IsolatedRootPath, "Linked.cs")));
    }

    [Fact]
    public void IsolatedCopyUsesPrivateDirectoryPermissionsOnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryDirectory();
        var input = fixture.CreateDirectory("input");
        var child = Path.Combine(input, "child");
        Directory.CreateDirectory(child);
        File.WriteAllText(Path.Combine(child, "Included.cs"), "internal sealed class Included { }");

        using IsolatedInput isolated = InputIsolator.CopyToTemporaryDirectory(
            input,
            CancellationToken.None);

        UnixFileMode privateMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Assert.Equal(privateMode, File.GetUnixFileMode(isolated.IsolatedRootPath));
        Assert.Equal(privateMode, File.GetUnixFileMode(Path.Combine(isolated.IsolatedRootPath, "child")));
    }

    [Fact]
    public void IsolatedCopyHonorsPreCanceledToken()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.CreateDirectory("input");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            InputIsolator.CopyToTemporaryDirectory(input, cancellation.Token));
    }

    [Fact]
    public void IsolatedCopyRejectsPerFileAndAggregateByteQuotasBeforeCopyingOffender()
    {
        using var fixture = new TemporaryDirectory();
        var perFileRoot = fixture.CreateDirectory("per-file");
        File.WriteAllText(Path.Combine(perFileRoot, "Large.cs"), "12345");

        InvalidDataException perFile = Assert.Throws<InvalidDataException>(() =>
            InputIsolator.CopyToTemporaryDirectory(
                perFileRoot,
                Limits(totalBytes: 10, fileBytes: 4),
                CancellationToken.None));

        Assert.Equal(
            "Input isolation limit exceeded at 'Large.cs': file size 5 bytes exceeds 4 bytes.",
            perFile.Message);

        var aggregateRoot = fixture.CreateDirectory("aggregate");
        File.WriteAllText(Path.Combine(aggregateRoot, "A.cs"), "123");
        File.WriteAllText(Path.Combine(aggregateRoot, "B.cs"), "456");

        InvalidDataException aggregate = Assert.Throws<InvalidDataException>(() =>
            InputIsolator.CopyToTemporaryDirectory(
                aggregateRoot,
                Limits(totalBytes: 5, fileBytes: 4),
                CancellationToken.None));

        Assert.Contains("total size exceeds 5 bytes", aggregate.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsolatedCopyRejectsFileDirectoryAndDepthQuotas()
    {
        using var fixture = new TemporaryDirectory();
        var fileRoot = fixture.CreateDirectory("files");
        File.WriteAllText(Path.Combine(fileRoot, "A.cs"), "a");
        File.WriteAllText(Path.Combine(fileRoot, "B.cs"), "b");

        InvalidDataException files = Assert.Throws<InvalidDataException>(() =>
            InputIsolator.CopyToTemporaryDirectory(
                fileRoot,
                Limits(fileCount: 1),
                CancellationToken.None));
        Assert.Contains("file count exceeds 1", files.Message, StringComparison.Ordinal);

        var directoryRoot = fixture.CreateDirectory("directories");
        Directory.CreateDirectory(Path.Combine(directoryRoot, "A"));
        Directory.CreateDirectory(Path.Combine(directoryRoot, "B"));

        InvalidDataException directories = Assert.Throws<InvalidDataException>(() =>
            InputIsolator.CopyToTemporaryDirectory(
                directoryRoot,
                Limits(directoryCount: 2),
                CancellationToken.None));
        Assert.Contains("directory count exceeds 2", directories.Message, StringComparison.Ordinal);

        var depthRoot = fixture.CreateDirectory("depth");
        Directory.CreateDirectory(Path.Combine(depthRoot, "one", "two"));

        InvalidDataException depth = Assert.Throws<InvalidDataException>(() =>
            InputIsolator.CopyToTemporaryDirectory(
                depthRoot,
                Limits(depth: 1),
                CancellationToken.None));
        Assert.Contains("directory depth exceeds 1", depth.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IsolatedCopyAcceptsTreeWithinExplicitBudget()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.CreateDirectory("bounded");
        Directory.CreateDirectory(Path.Combine(input, "src"));
        File.WriteAllText(Path.Combine(input, "src", "A.cs"), "abc");

        using IsolatedInput isolated = InputIsolator.CopyToTemporaryDirectory(
            input,
            Limits(
                totalBytes: 3,
                fileBytes: 3,
                fileCount: 1,
                directoryCount: 2,
                depth: 1),
            CancellationToken.None);

        Assert.Equal(
            "abc",
            File.ReadAllText(Path.Combine(isolated.IsolatedRootPath, "src", "A.cs")));
    }

    [Fact]
    public void SourceDiscoveryHonorsPreCanceledToken()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.CreateDirectory("input");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            SourceFileDiscovery.Discover(
                input,
                includeGeneratedCode: false,
                cancellationToken: cancellation.Token));
    }

    private static InputIsolationLimits Limits(
        long totalBytes = 100,
        long fileBytes = 100,
        int fileCount = 10,
        int directoryCount = 10,
        int depth = 10)
    {
        return new InputIsolationLimits(
            totalBytes,
            fileBytes,
            fileCount,
            directoryCount,
            depth);
    }

    [Fact]
    public async Task DirectoryWithoutProjectUsesRelocatableSyntheticProjectAndConnectedGraph()
    {
        using var fixture = new TemporaryDirectory();
        var firstRoot = fixture.CreateDirectory("first");
        var secondRoot = fixture.CreateDirectory("second");
        const string source = "namespace Sample; internal sealed class Relocatable { public void Run() { } }";
        File.WriteAllText(Path.Combine(firstRoot, "Relocatable.cs"), source);
        File.WriteAllText(Path.Combine(secondRoot, "Relocatable.cs"), source);

        DiscoveredSources firstSources = SourceFileDiscovery.Discover(firstRoot, includeGeneratedCode: false);
        DiscoveredSources secondSources = SourceFileDiscovery.Discover(secondRoot, includeGeneratedCode: false);
        SyntaxAnalysisFacts firstFacts = await SyntaxFactsCollector.CollectAsync(
            firstSources,
            useSemantic: false,
            noRestore: true,
            CancellationToken.None);
        SyntaxAnalysisFacts secondFacts = await SyntaxFactsCollector.CollectAsync(
            secondSources,
            useSemantic: false,
            noRestore: true,
            CancellationToken.None);

        Assert.Equal(["."], firstSources.ProjectPaths);
        Assert.Equal(
            firstFacts.Types.Select(type => type.TargetId),
            secondFacts.Types.Select(type => type.TargetId));
        Assert.Equal(
            firstFacts.Members.Select(member => member.TargetId),
            secondFacts.Members.Select(member => member.TargetId));

        GraphArtifact graph = GraphProjector.Project(firstFacts);
        GraphNodeLine project = Assert.Single(graph.Nodes, node => node.Kind == "project");
        GraphNodeLine file = Assert.Single(graph.Nodes, node => node.Kind == "file");
        Assert.Equal(".", project.FilePath);
        Assert.Equal("source-tree", project.Name);
        Assert.Contains(graph.Edges, edge =>
            edge.From == "solution:root" && edge.To == project.Id && edge.Kind == "contains");
        Assert.Contains(graph.Edges, edge =>
            edge.From == project.Id && edge.To == file.Id && edge.Kind == "contains");
    }

    [Fact]
    public void ExplicitSolutionScopesDiscoveryToItsProjects()
    {
        using var fixture = new TemporaryDirectory();
        var root = fixture.CreateDirectory("input");
        var projectA = Path.Combine(root, "A");
        var projectB = Path.Combine(root, "B");
        Directory.CreateDirectory(projectA);
        Directory.CreateDirectory(projectB);
        File.WriteAllText(Path.Combine(projectA, "A.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(projectA, "A.cs"), "internal sealed class A { }");
        File.WriteAllText(Path.Combine(projectB, "B.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(Path.Combine(projectB, "B.cs"), "internal sealed class B { }");
        var solutionPath = Path.Combine(root, "Selected.sln");
        File.WriteAllText(
            solutionPath,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"A\\A.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\n");

        DiscoveredSources discovered = SourceFileDiscovery.Discover(
            solutionPath,
            includeGeneratedCode: false);

        Assert.Equal(["A/A.csproj"], discovered.ProjectPaths);
        Assert.Equal(["A/A.cs"], discovered.SourceFiles.Select(file => file.RelativePath));
        Assert.Equal("Selected.sln", discovered.SelectedSolutionPath);
    }

    [Fact]
    public void NestedSolutionUsesCommonRootAndIsolationKeepsReferencedProjects()
    {
        using var fixture = new TemporaryDirectory();
        var root = fixture.CreateDirectory("input");
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var solutionDirectory = Path.Combine(root, "solutions");
        var projectDirectory = Path.Combine(root, "src", "A");
        Directory.CreateDirectory(solutionDirectory);
        Directory.CreateDirectory(projectDirectory);
        File.WriteAllText(
            Path.Combine(projectDirectory, "A.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        File.WriteAllText(
            Path.Combine(projectDirectory, "A.cs"),
            "internal sealed class A { }");
        var solutionPath = Path.Combine(solutionDirectory, "Selected.sln");
        File.WriteAllText(
            solutionPath,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"A\", \"..\\src\\A\\A.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\n");

        DiscoveredSources discovered = SourceFileDiscovery.Discover(
            solutionPath,
            includeGeneratedCode: false);
        using IsolatedInput isolated = InputIsolator.CopyToTemporaryDirectory(
            solutionPath,
            CancellationToken.None);

        Assert.Equal(Path.GetFullPath(root), discovered.RootPath);
        Assert.Equal("solutions/Selected.sln", discovered.SelectedSolutionPath);
        Assert.Equal(["src/A/A.csproj"], discovered.ProjectPaths);
        Assert.Equal(["src/A/A.cs"], discovered.SourceFiles.Select(file => file.RelativePath));
        Assert.True(File.Exists(isolated.IsolatedInputPath));
        Assert.True(File.Exists(Path.Combine(isolated.IsolatedRootPath, "src", "A", "A.csproj")));
    }

    [Fact]
    public void SolutionCannotExpandAnalysisRootWithoutRepositoryBoundary()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.CreateDirectory("input");
        var solutionDirectory = Path.Combine(input, "solutions");
        var outsideDirectory = fixture.CreateDirectory("outside");
        Directory.CreateDirectory(solutionDirectory);
        File.WriteAllText(
            Path.Combine(outsideDirectory, "Outside.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var solutionPath = Path.Combine(solutionDirectory, "Escaping.sln");
        File.WriteAllText(
            solutionPath,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Outside\", \"..\\..\\outside\\Outside.csproj\", \"{11111111-1111-1111-1111-111111111111}\"\nEndProject\n");

        ArgumentException exception = Assert.Throws<ArgumentException>(() =>
            SourceFileDiscovery.Discover(solutionPath, includeGeneratedCode: false));

        Assert.Contains("outside its directory", exception.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            $"CodeMetricsToolkit-discovery-{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(_path);
        }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(_path, name);
            Directory.CreateDirectory(path);

            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_path))
            {
                Directory.Delete(_path, recursive: true);
            }
        }
    }
}
