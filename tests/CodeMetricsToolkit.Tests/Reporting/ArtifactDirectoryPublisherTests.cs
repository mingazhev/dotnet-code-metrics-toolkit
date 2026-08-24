using CodeMetricsToolkit.Core;
using CodeMetricsToolkit.Core.Reporting;
using CodeMetricsToolkit.Core.Validation;
using CodeMetricsToolkit.Tests.SchemaValidation;
using CodeMetricsToolkit.Abstractions;

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
    public async Task SuccessfulReplacementWarnsWithExactPathWhenBackupCleanupFails()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreatePreviousOutput();
        string? leftoverPath = null;

        IReadOnlyList<string> warnings = await ArtifactDirectoryPublisher.PublishAsync(
            workspace.OutputPath,
            CopyValidFixtureAsync,
            path =>
            {
                if (path.Contains(".backup-", StringComparison.Ordinal))
                {
                    leftoverPath = path;
                    throw new IOException("simulated backup cleanup failure");
                }

                Directory.Delete(path, recursive: true);
            },
            CancellationToken.None);

        var warning = Assert.Single(warnings);
        Assert.NotNull(leftoverPath);
        Assert.Contains(leftoverPath, warning, StringComparison.Ordinal);
        Assert.Contains("backup", warning, StringComparison.Ordinal);
        Assert.True(Directory.Exists(leftoverPath));
        Assert.True(File.Exists(Path.Combine(workspace.OutputPath, "manifest.json")));
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
    public async Task WriterAndStagingCleanupFailuresPreserveBothErrorsAndRevealLeftoverPath()
    {
        using var workspace = new TemporaryWorkspace();
        var markerPath = workspace.CreatePreviousOutput();
        string? leftoverPath = null;

        AggregateException exception = await Assert.ThrowsAsync<AggregateException>(() =>
            ArtifactDirectoryPublisher.PublishAsync(
                workspace.OutputPath,
                (stagingPath, _) =>
                {
                    File.WriteAllText(
                        Path.Combine(stagingPath, "chunks.ndjson"),
                        "{\"text\":\"sensitive source text\"}");
                    throw new InvalidOperationException("simulated writer failure");
                },
                path =>
                {
                    leftoverPath = path;
                    throw new IOException("simulated staging cleanup failure");
                },
                CancellationToken.None));

        Assert.Collection(
            exception.InnerExceptions,
            failure => Assert.Equal("simulated writer failure", failure.Message),
            cleanup => Assert.Contains(
                "simulated staging cleanup failure",
                cleanup.Message,
                StringComparison.Ordinal));
        Assert.NotNull(leftoverPath);
        Assert.Contains(leftoverPath, exception.Message, StringComparison.Ordinal);
        Assert.Contains("source text", exception.Message, StringComparison.Ordinal);
        Assert.Equal("previous output", File.ReadAllText(markerPath));
        Assert.Equal(
            "{\"text\":\"sensitive source text\"}",
            File.ReadAllText(Path.Combine(leftoverPath, "chunks.ndjson")));
    }

    [Fact]
    public async Task CancellationAndStagingCleanupFailureRemainCancellation()
    {
        using var workspace = new TemporaryWorkspace();
        workspace.CreatePreviousOutput();
        using var cancellation = new CancellationTokenSource();
        string? leftoverPath = null;

        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ArtifactDirectoryPublisher.PublishAsync(
                workspace.OutputPath,
                (stagingPath, cancellationToken) =>
                {
                    File.WriteAllText(Path.Combine(stagingPath, "partial.json"), "{}");
                    cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                },
                path =>
                {
                    leftoverPath = path;
                    throw new IOException("simulated staging cleanup failure");
                },
                cancellation.Token));

        Assert.NotNull(leftoverPath);
        Assert.Contains(leftoverPath, exception.Message, StringComparison.Ordinal);
        Assert.IsType<IOException>(exception.InnerException);
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
    public async Task ExistingManifestSymlinkCannotBeUsedToReplaceOutput()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(workspace.OutputPath);
        File.WriteAllText(Path.Combine(workspace.OutputPath, "keep.txt"), "keep");
        var realManifest = Path.Combine(workspace.RootPath, "outside-manifest.json");
        File.WriteAllText(realManifest, $$"""{"tool":"{{ToolkitInfo.Name}}"}""");
        File.CreateSymbolicLink(Path.Combine(workspace.OutputPath, "manifest.json"), realManifest);
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

        Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.Ordinal);
        Assert.False(writerCalled);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(workspace.OutputPath, "keep.txt")));
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
    public async Task SymbolicLinkInOutputParentChainIsRejectedBeforeWriting()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TemporaryWorkspace();
        var externalPath = workspace.RootPath + "-external";
        Directory.CreateDirectory(externalPath);
        var externalMarker = Path.Combine(externalPath, "keep.txt");
        File.WriteAllText(externalMarker, "unchanged");

        try
        {
            var linkedParent = Path.Combine(workspace.RootPath, "linked-output");
            Directory.CreateSymbolicLink(linkedParent, externalPath);
            var outputPath = Path.Combine(linkedParent, "artifacts");
            var writerCalled = false;

            IOException exception = await Assert.ThrowsAsync<IOException>(() =>
                ArtifactDirectoryPublisher.PublishAsync(
                    outputPath,
                    (_, _) =>
                    {
                        writerCalled = true;
                        return Task.CompletedTask;
                    },
                    CancellationToken.None));

            Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.Ordinal);
            Assert.False(writerCalled);
            Assert.Equal("unchanged", File.ReadAllText(externalMarker));
            Assert.Equal([externalMarker], Directory.EnumerateFiles(externalPath).ToArray());
            Assert.Empty(Directory.EnumerateDirectories(externalPath));
        }
        finally
        {
            Directory.Delete(externalPath, recursive: true);
        }
    }

    [Fact]
    public async Task RootLevelSystemAliasDoesNotRejectSafeTemporaryOutput()
    {
        if (!OperatingSystem.IsMacOS() || !Directory.Exists("/var/folders"))
        {
            return;
        }

        var aliasedTemporaryRoot = "/var" + Path.GetTempPath()["/private/var".Length..];
        if (!Directory.Exists(aliasedTemporaryRoot))
        {
            return;
        }
        var rootPath = Path.Combine(
            aliasedTemporaryRoot,
            $"CodeMetricsToolkit-system-alias-{Guid.NewGuid():N}");
        var outputPath = Path.Combine(rootPath, "artifacts");
        Directory.CreateDirectory(rootPath);

        try
        {
            await ArtifactDirectoryPublisher.PublishAsync(
                outputPath,
                CopyValidFixtureAsync,
                CancellationToken.None);

            Assert.True(File.Exists(Path.Combine(outputPath, ArtifactNames.Manifest)));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
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
