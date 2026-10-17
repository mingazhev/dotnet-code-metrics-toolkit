using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class SemanticWorkspaceLoaderTests
{
    [Fact]
    public void OperationalIoFailuresAreDegradedRatherThanInternalErrors()
    {
        Assert.True(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new IOException("disk")));
        Assert.True(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new UnauthorizedAccessException()));
        Assert.True(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new InvalidOperationException("workspace")));
    }

    [Fact]
    public void ContractAndLifetimeFailuresAreNotSwallowedAsDegradedWorkspaceLoad()
    {
        Assert.False(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new ArgumentException("contract")));
        Assert.False(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new ArgumentNullException("value")));
        Assert.False(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new ObjectDisposedException("workspace")));
    }

    [Fact]
    public void RelativeProjectPathRejectsActualExternalRootsAndKeepsDottedNamesInsideRoot()
    {
        using var fixture = new TemporaryDirectory();
        var root = fixture.CreateDirectory("analysis");
        var dottedProject = Path.Combine(root, "..App", "App.csproj");
        var externalProject = Path.Combine(fixture.Path, "outside", "Outside.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(dottedProject)!);
        Directory.CreateDirectory(Path.GetDirectoryName(externalProject)!);
        File.WriteAllText(dottedProject, "<Project />");
        File.WriteAllText(externalProject, "<Project />");

        Assert.Equal("..App/App.csproj", SemanticWorkspaceLoader.ToRelativeProjectPath(root, dottedProject));
        Assert.Null(SemanticWorkspaceLoader.ToRelativeProjectPath(root, externalProject));
    }

    [Fact]
    public void RelativeProjectPathRejectsRootedResultForDifferentWindowsDrive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var currentDrive = Path.GetPathRoot(Path.GetTempPath())![0];
        var otherDrive = currentDrive == 'C' ? 'D' : 'C';
        var root = $"{currentDrive}:\\analysis";
        var externalProject = $"{otherDrive}:\\external\\Outside.csproj";
        var relativePath = Path.GetRelativePath(root, externalProject);

        Assert.True(Path.IsPathRooted(relativePath));
        Assert.Null(SemanticWorkspaceLoader.ToRelativeProjectPath(root, externalProject));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeMetricsToolkit-workspace-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string CreateDirectory(string name)
        {
            var directory = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}
