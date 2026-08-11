namespace CodeMetricsToolkit.Core.Discovery;

public static class InputIsolator
{
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false
    };

    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        ".vs",
        "bin",
        "obj",
        "artifacts"
    ];

    public static IsolatedInput CopyToTemporaryDirectory(
        string inputPath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        cancellationToken.ThrowIfCancellationRequested();

        InputSelection selection = SourceFileDiscovery.ResolveInputSelection(inputPath);
        var sourceRoot = selection.RootPath;
        var selectedRelativePath = selection.SelectedSolutionPath ?? selection.SelectedProjectPath;
        var tempRoot = Path.Combine(Path.GetTempPath(), "codemetrics-input-" + Guid.NewGuid().ToString("N"));

        try
        {
            CreatePrivateDirectory(tempRoot);
            CopyDirectory(sourceRoot, tempRoot, cancellationToken);
        }
        catch
        {
            _ = TryDeleteDirectory(tempRoot, out _);
            throw;
        }

        var isolatedInputPath = selectedRelativePath is null
            ? tempRoot
            : Path.Combine(tempRoot, selectedRelativePath);

        return new IsolatedInput(sourceRoot, tempRoot, isolatedInputPath);
    }

    private static void CopyDirectory(
        string sourceDirectory,
        string targetDirectory,
        CancellationToken cancellationToken)
    {
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", EnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryName = Path.GetFileName(directory);
            if (ExcludedDirectoryNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            var targetChildDirectory = Path.Combine(targetDirectory, directoryName);
            CreatePrivateDirectory(targetChildDirectory);
            CopyDirectory(directory, targetChildDirectory, cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", EnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, targetFile);
        }
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }

        Directory.CreateDirectory(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    internal static bool TryDeleteDirectory(string path, out string? failureMessage)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }

            failureMessage = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            failureMessage = $"Could not remove isolated input directory '{path}': {exception.Message}";
            return false;
        }
    }
}

public sealed record IsolatedInput(
    string OriginalRootPath,
    string IsolatedRootPath,
    string IsolatedInputPath) : IDisposable
{
    public bool TryDispose(out string? failureMessage)
    {
        return InputIsolator.TryDeleteDirectory(IsolatedRootPath, out failureMessage);
    }

    public void Dispose()
    {
        if (!TryDispose(out var failureMessage))
        {
            System.Diagnostics.Trace.TraceWarning(failureMessage);
        }
    }
}
