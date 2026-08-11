using CodeMetricsToolkit.Core.Discovery;

namespace CodeMetricsToolkit.Tests.Discovery;

public sealed class DiscoverySafetyTests
{
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
