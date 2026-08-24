using System.Text.Json;
using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Validation;

namespace CodeMetricsToolkit.Core.Reporting;

/// <summary>
/// Publishes a complete, validated artifact set without exposing partially written output.
/// </summary>
public static class ArtifactDirectoryPublisher
{
    internal delegate void DeleteDirectory(string path);

    /// <summary>
    /// Writes artifacts to a sibling staging directory, validates them, and replaces the output
    /// directory by same-volume directory renames.
    /// </summary>
    /// <param name="outputPath">The final artifact directory.</param>
    /// <param name="writeArtifactsAsync">
    /// A callback that writes the complete artifact set to the supplied staging directory.
    /// </param>
    /// <param name="cancellationToken">Cancellation token observed before publication starts.</param>
    /// <returns>
    /// Warnings for temporary directories that could not be removed after successful publication.
    /// Each warning identifies the leftover directory.
    /// </returns>
    /// <exception cref="IOException">
    /// <paramref name="outputPath"/> is an existing file or publication cannot be completed.
    /// </exception>
    /// <exception cref="InvalidDataException">The staged artifact set is invalid.</exception>
    public static Task<IReadOnlyList<string>> PublishAsync(
        string outputPath,
        Func<string, CancellationToken, Task> writeArtifactsAsync,
        CancellationToken cancellationToken)
    {
        return PublishAsync(
            outputPath,
            writeArtifactsAsync,
            static path => Directory.Delete(path, recursive: true),
            cancellationToken);
    }

    internal static async Task<IReadOnlyList<string>> PublishAsync(
        string outputPath,
        Func<string, CancellationToken, Task> writeArtifactsAsync,
        DeleteDirectory deleteDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(writeArtifactsAsync);
        ArgumentNullException.ThrowIfNull(deleteDirectory);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedOutputPath = NormalizeOutputPath(outputPath);
        ValidateOutputAncestors(normalizedOutputPath);
        ValidateOutputTarget(normalizedOutputPath);

        var parentPath = Path.GetDirectoryName(normalizedOutputPath)!;
        var outputDirectoryName = Path.GetFileName(normalizedOutputPath);

        Directory.CreateDirectory(parentPath);
        ValidateOutputAncestors(normalizedOutputPath);
        ValidateOutputTarget(normalizedOutputPath);

        var stagingPath = CreateUnusedSiblingPath(
            parentPath,
            outputDirectoryName,
            "staging");

        CreateStagingDirectory(stagingPath, normalizedOutputPath);

        var warnings = new List<string>();
        try
        {
            await writeArtifactsAsync(stagingPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            OutputValidationResult validation = OutputValidator.Validate(stagingPath, cancellationToken);

            if (!validation.IsValid)
            {
                throw new InvalidDataException(
                    $"Staged artifact set is invalid:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, validation.Errors));
            }

            cancellationToken.ThrowIfCancellationRequested();
            PublishStagedDirectory(
                normalizedOutputPath,
                stagingPath,
                parentPath,
                outputDirectoryName,
                deleteDirectory,
                warnings);
        }
        catch (Exception publicationFailure)
        {
            IOException? cleanupFailure = TryDeleteDirectory(
                stagingPath,
                "staging",
                deleteDirectory);

            if (cleanupFailure is not null)
            {
                if (publicationFailure is OperationCanceledException cancellation)
                {
                    throw new OperationCanceledException(
                        $"{cancellation.Message} Artifact staging cleanup also failed; " +
                        $"generated artifacts may remain at '{stagingPath}', including source text " +
                        "when chunk text output is enabled.",
                        cleanupFailure,
                        cancellation.CancellationToken);
                }

                throw new AggregateException(
                    $"Artifact publication failed, and staging cleanup also failed. " +
                    $"Generated artifacts may remain at '{stagingPath}', including source text " +
                    "when chunk text output is enabled.",
                    publicationFailure,
                    cleanupFailure);
            }

            throw;
        }

        IOException? stagingCleanupFailure = TryDeleteDirectory(
            stagingPath,
            "staging",
            deleteDirectory);

        if (stagingCleanupFailure is not null)
        {
            warnings.Add(stagingCleanupFailure.Message);
        }

        return warnings;
    }

    private static string NormalizeOutputPath(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var normalizedPath = Path.TrimEndingDirectorySeparator(fullPath);
        var parentPath = Path.GetDirectoryName(normalizedPath);

        if (parentPath is null || string.IsNullOrEmpty(Path.GetFileName(normalizedPath)))
        {
            throw new ArgumentException(
                "The filesystem root cannot be used as an artifact output directory.",
                nameof(outputPath));
        }

        return normalizedPath;
    }

    private static void ValidateOutputTarget(string outputPath)
    {
        if (File.Exists(outputPath))
        {
            throw new IOException($"Artifact output path is an existing file: {outputPath}");
        }

        if (!Directory.Exists(outputPath))
        {
            return;
        }

        FileAttributes attributes = File.GetAttributes(outputPath);

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new IOException(
                $"Artifact output path is a symbolic link or reparse point and cannot be replaced: " +
                outputPath);
        }

        // Pre-created empty output directories are common in CLI scripts and contain no
        // user data to lose. Any non-empty directory still needs our manifest marker.
        if (!Directory.EnumerateFileSystemEntries(outputPath).Any())
        {
            return;
        }

        var manifestPath = Path.Combine(outputPath, ArtifactNames.Manifest);

        if (!File.Exists(manifestPath))
        {
            throw UnrecognizedOutputDirectory(outputPath);
        }

        try
        {
            if ((File.GetAttributes(manifestPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException(
                    $"Artifact output manifest is a symbolic link or reparse point and cannot be used: " +
                    manifestPath);
            }

            var manifestBytes = ValidationInputReader.ReadJsonBytes(
                manifestPath,
                ValidationInputLimits.DefaultMaxJsonArtifactBytes);
            using var manifest = JsonDocument.Parse(
                manifestBytes,
                new JsonDocumentOptions { MaxDepth = ValidationInputLimits.DefaultMaxJsonDepth });
            JsonElement root = manifest.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tool", out JsonElement tool) ||
                tool.ValueKind != JsonValueKind.String ||
                !string.Equals(tool.GetString(), ToolkitInfo.Name, StringComparison.Ordinal))
            {
                throw UnrecognizedOutputDirectory(outputPath);
            }
        }
        catch (Exception exception) when (exception is
            JsonException or
            UnauthorizedAccessException or
            InvalidDataException)
        {
            throw new IOException(
                $"Artifact output directory has an unreadable manifest and cannot be replaced: " +
                outputPath,
                exception);
        }
    }

    private static void ValidateOutputAncestors(string outputPath)
    {
        for (DirectoryInfo? directory = Directory.GetParent(outputPath);
             directory is not null;
             directory = directory.Parent)
        {
            FileAttributes attributes;

            try
            {
                attributes = File.GetAttributes(directory.FullName);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                throw new IOException(
                    $"Artifact output parent path could not be inspected: {directory.FullName}",
                    exception);
            }

            if ((attributes & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            // macOS exposes stable OS-owned aliases such as /var and /tmp at the
            // filesystem root. Deeper links can be controlled by the analyzed tree.
            if (directory.Parent?.Parent is null)
            {
                continue;
            }

            throw new IOException(
                $"Artifact output parent path contains a symbolic link or reparse point: " +
                directory.FullName);
        }
    }

    private static IOException UnrecognizedOutputDirectory(string outputPath)
    {
        return new IOException(
            $"Refusing to replace unrecognized directory '{outputPath}'. " +
            $"An existing artifact output must contain {ArtifactNames.Manifest} with " +
            $"tool '{ToolkitInfo.Name}'.");
    }

    private static string CreateUnusedSiblingPath(
        string parentPath,
        string outputDirectoryName,
        string purpose)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = Path.Combine(
                parentPath,
                $".{outputDirectoryName}.{purpose}-{Guid.NewGuid():N}");

            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException(
            $"Could not reserve a sibling {purpose} path for artifact output '{outputDirectoryName}'.");
    }

    private static void PublishStagedDirectory(
        string outputPath,
        string stagingPath,
        string parentPath,
        string outputDirectoryName,
        DeleteDirectory deleteDirectory,
        List<string> warnings)
    {
        ValidateOutputAncestors(outputPath);
        ValidateOutputTarget(outputPath);

        if (!Directory.Exists(outputPath))
        {
            Directory.Move(stagingPath, outputPath);
            return;
        }

        var backupPath = CreateUnusedSiblingPath(
            parentPath,
            outputDirectoryName,
            "backup");

        Directory.Move(outputPath, backupPath);

        try
        {
            Directory.Move(stagingPath, outputPath);
        }
        catch (Exception publishException)
        {
            try
            {
                Directory.Move(backupPath, outputPath);
            }
            catch (Exception rollbackException)
            {
                throw new AggregateException(
                    $"Artifact publication failed and the previous output could not be restored. " +
                    $"The previous output remains at '{backupPath}'.",
                    publishException,
                    rollbackException);
            }

            throw;
        }

        IOException? backupCleanupFailure = TryDeleteDirectory(
            backupPath,
            "backup",
            deleteDirectory);

        if (backupCleanupFailure is not null)
        {
            warnings.Add(backupCleanupFailure.Message);
        }
    }

    private static void CreateStagingDirectory(string stagingPath, string outputPath)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(stagingPath);
            return;
        }

        UnixFileMode mode = Directory.Exists(outputPath)
            ? File.GetUnixFileMode(outputPath)
            : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(stagingPath, mode);
        File.SetUnixFileMode(stagingPath, mode);
    }

    private static IOException? TryDeleteDirectory(
        string path,
        string purpose,
        DeleteDirectory deleteDirectory)
    {
        try
        {
            if (Directory.Exists(path))
            {
                deleteDirectory(path);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new IOException(
                $"Could not remove artifact {purpose} directory '{path}': {exception.Message}",
                exception);
        }
    }
}
