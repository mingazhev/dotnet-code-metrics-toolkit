using System.Text.Json;
using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Validation;

namespace CodeMetricsToolkit.Core.Reporting;

/// <summary>
/// Publishes a complete, validated artifact set without exposing partially written output.
/// </summary>
public static class ArtifactDirectoryPublisher
{
    /// <summary>
    /// Writes artifacts to a sibling staging directory, validates them, and replaces the output
    /// directory by same-volume directory renames.
    /// </summary>
    /// <param name="outputPath">The final artifact directory.</param>
    /// <param name="writeArtifactsAsync">
    /// A callback that writes the complete artifact set to the supplied staging directory.
    /// </param>
    /// <param name="cancellationToken">Cancellation token observed before publication starts.</param>
    /// <exception cref="IOException">
    /// <paramref name="outputPath"/> is an existing file or publication cannot be completed.
    /// </exception>
    /// <exception cref="InvalidDataException">The staged artifact set is invalid.</exception>
    public static async Task PublishAsync(
        string outputPath,
        Func<string, CancellationToken, Task> writeArtifactsAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(writeArtifactsAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var normalizedOutputPath = NormalizeOutputPath(outputPath);
        ValidateOutputTarget(normalizedOutputPath);

        var parentPath = Path.GetDirectoryName(normalizedOutputPath)!;
        var outputDirectoryName = Path.GetFileName(normalizedOutputPath);

        Directory.CreateDirectory(parentPath);
        ValidateOutputTarget(normalizedOutputPath);

        var stagingPath = CreateUnusedSiblingPath(
            parentPath,
            outputDirectoryName,
            "staging");

        CreateStagingDirectory(stagingPath, normalizedOutputPath);

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
            PublishStagedDirectory(normalizedOutputPath, stagingPath, parentPath, outputDirectoryName);
        }
        finally
        {
            TryDeleteDirectory(stagingPath);
        }
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
            using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
            JsonElement root = manifest.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tool", out JsonElement tool) ||
                tool.ValueKind != JsonValueKind.String ||
                !string.Equals(tool.GetString(), ToolkitInfo.Name, StringComparison.Ordinal))
            {
                throw UnrecognizedOutputDirectory(outputPath);
            }
        }
        catch (Exception exception) when (exception is JsonException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Artifact output directory has an unreadable manifest and cannot be replaced: " +
                outputPath,
                exception);
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
        string outputDirectoryName)
    {
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

        TryDeleteDirectory(backupPath);
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

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Publication is already safe; temporary-directory cleanup is best effort.
        }
        catch (UnauthorizedAccessException)
        {
            // Publication is already safe; temporary-directory cleanup is best effort.
        }
    }
}
