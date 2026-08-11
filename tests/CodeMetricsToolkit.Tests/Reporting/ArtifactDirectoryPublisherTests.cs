using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Validation;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class ArtifactDirectoryPublisherTests
{
    [Fact]
    public async Task InitialPublishCreatesValidatedOutput()
    {
        using var workspace = new TemporaryWorkspace();

        await ArtifactDirectoryPublisher.PublishAsync(
            workspace.OutputPath,
            CopyValidFixtureAsync,
            CancellationToken.None);

        OutputValidationResult validation = OutputValidator.Validate(
            workspace.OutputPath,
            CancellationToken.None);

        Assert.True(validation.IsValid, string.Join(Environment.NewLine, validation.Errors));
        AssertOnlyPublishedOutputRemains(workspace);
    }

    [Fact]
    public async Task InitialPublishAcceptsPreCreatedEmptyDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(workspace.OutputPath);

        await ArtifactDirectoryPublisher.PublishAsync(
            workspace.OutputPath,
            CopyValidFixtureAsync,
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(workspace.OutputPath, "manifest.json")));
        AssertOnlyPublishedOutputRemains(workspace);
    }

    [Fact]
    public async Task PublishPreservesExistingUnixDirectoryPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(workspace.OutputPath);
        UnixFileMode expectedMode = UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupWrite |
            UnixFileMode.GroupExecute;
        File.SetUnixFileMode(workspace.OutputPath, expectedMode);

        await ArtifactDirectoryPublisher.PublishAsync(
            workspace.OutputPath,
            CopyValidFixtureAsync,
            CancellationToken.None);

        Assert.Equal(expectedMode, File.GetUnixFileMode(workspace.OutputPath));
    }

    [Fact]
    public async Task ReplacementRemovesFilesFromPreviousOutput()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreatePreviousOutput();
        File.WriteAllText(Path.Combine(workspace.OutputPath, "stale.txt"), "old output");

        await ArtifactDirectoryPublisher.PublishAsync(
            workspace.OutputPath,
            CopyValidFixtureAsync,
            CancellationToken.None);

        Assert.False(File.Exists(Path.Combine(workspace.OutputPath, "stale.txt")));
        Assert.True(File.Exists(Path.Combine(workspace.OutputPath, "manifest.json")));
        AssertOnlyPublishedOutputRemains(workspace);
    }

    [Fact]
    public async Task WriterFailurePreservesPreviousOutputAndCleansStaging()
    {
        using var workspace = new TemporaryWorkspace();
        var markerPath = workspace.CreatePreviousOutput();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ArtifactDirectoryPublisher.PublishAsync(
                workspace.OutputPath,
                (stagingPath, _) =>
                {
                    File.WriteAllText(Path.Combine(stagingPath, "partial.json"), "{}");
                    throw new InvalidOperationException("simulated writer failure");
                },
                CancellationToken.None));

        Assert.Equal("previous output", File.ReadAllText(markerPath));
        AssertOnlyPublishedOutputRemains(workspace);
    }

    [Fact]
    public async Task ValidationFailurePreservesPreviousOutputAndCleansStaging()
    {
        using var workspace = new TemporaryWorkspace();
        var markerPath = workspace.CreatePreviousOutput();

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            ArtifactDirectoryPublisher.PublishAsync(
                workspace.OutputPath,
                (stagingPath, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.WriteAllText(Path.Combine(stagingPath, "manifest.json"), "{}");
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Contains("Staged artifact set is invalid", exception.Message, StringComparison.Ordinal);
        Assert.Equal("previous output", File.ReadAllText(markerPath));
        AssertOnlyPublishedOutputRemains(workspace);
    }

    [Fact]
    public async Task CancellationPreservesPreviousOutputAndCleansStaging()
    {
        using var workspace = new TemporaryWorkspace();
        var markerPath = workspace.CreatePreviousOutput();
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ArtifactDirectoryPublisher.PublishAsync(
                workspace.OutputPath,
                (stagingPath, cancellationToken) =>
                {
                    File.WriteAllText(Path.Combine(stagingPath, "partial.json"), "{}");
                    cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                cancellation.Token));

        Assert.Equal("previous output", File.ReadAllText(markerPath));
        AssertOnlyPublishedOutputRemains(workspace);
    }

    [Fact]
    public async Task ExistingFileAtOutputPathIsRejectedBeforeWriting()
    {
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(workspace.OutputPath, "not a directory");
        var writerCalled = false;

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            ArtifactDirectoryPublisher.PublishAsync(
                workspace.OutputPath,
                (_, _) =>
                {
                    writerCalled = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Contains("existing file", exception.Message, StringComparison.Ordinal);
        Assert.False(writerCalled);
        Assert.Equal("not a directory", File.ReadAllText(workspace.OutputPath));
    }

    [Fact]
    public async Task UnrecognizedDirectoryIsRejectedBeforeWriting()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(workspace.OutputPath);
        var markerPath = Path.Combine(workspace.OutputPath, "user-data.txt");
        File.WriteAllText(markerPath, "do not replace");
        var writerCalled = false;

        IOException exception = await Assert.ThrowsAsync<IOException>(() =>
            ArtifactDirectoryPublisher.PublishAsync(
                workspace.OutputPath,
                (_, _) =>
                {
                    writerCalled = true;
                    return Task.CompletedTask;
                },
                CancellationToken.None));

        Assert.Contains("Refusing to replace unrecognized directory", exception.Message);
        Assert.False(writerCalled);
        Assert.Equal("do not replace", File.ReadAllText(markerPath));
        AssertOnlyPublishedOutputRemains(workspace);
    }

    private static Task CopyValidFixtureAsync(
        string stagingPath,
        CancellationToken cancellationToken)
    {
        CopyValidFixture(stagingPath, cancellationToken);
        return Task.CompletedTask;
    }

    private static void CopyValidFixture(
        string destinationPath,
        CancellationToken cancellationToken)
    {
        var fixturePath = Path.Combine(
            SchemaAssertions.RepositoryRoot(),
            "tests",
            "CodeMetricsToolkit.Tests",
            "Fixtures",
            "ExpectedOutput",
            "ValidMinimal");

        foreach (var sourcePath in Directory.EnumerateFiles(fixturePath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(sourcePath, Path.Combine(destinationPath, Path.GetFileName(sourcePath)));
        }
    }

    private static void AssertOnlyPublishedOutputRemains(TemporaryWorkspace workspace)
    {
        Assert.Equal(
            [workspace.OutputPath],
            Directory.EnumerateDirectories(workspace.RootPath).ToArray());
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"CodeMetricsToolkit-publish-{Guid.NewGuid():N}");
            OutputPath = Path.Combine(RootPath, "artifacts");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string OutputPath { get; }

        public string CreatePreviousOutput()
        {
            Directory.CreateDirectory(OutputPath);
            CopyValidFixture(OutputPath, CancellationToken.None);
            var markerPath = Path.Combine(OutputPath, "previous.txt");
            File.WriteAllText(markerPath, "previous output");
            return markerPath;
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
