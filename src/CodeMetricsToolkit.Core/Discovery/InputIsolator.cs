namespace CodeMetricsToolkit.Core.Discovery;

public static class InputIsolator
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        ".vs",
        "bin",
        "obj",
        "artifacts"
    ];

    public static IsolatedInput CopyToTemporaryDirectory(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        string sourceRoot = ResolveRootPath(inputPath);
        string tempRoot = Path.Combine(Path.GetTempPath(), "codemetrics-input-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempRoot);
        CopyDirectory(sourceRoot, tempRoot);

        return new IsolatedInput(sourceRoot, tempRoot);
    }

    private static string ResolveRootPath(string inputPath)
    {
        string fullPath = Path.GetFullPath(inputPath);

        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath) ??
                throw new DirectoryNotFoundException($"Could not resolve directory for {inputPath}");
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"Input path does not exist: {inputPath}");
        }

        return fullPath;
    }

    private static void CopyDirectory(string sourceDirectory, string targetDirectory)
    {
        foreach (string directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string directoryName = Path.GetFileName(directory);
            if (ExcludedDirectoryNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            string targetChildDirectory = Path.Combine(targetDirectory, directoryName);
            Directory.CreateDirectory(targetChildDirectory);
            CopyDirectory(directory, targetChildDirectory);
        }

        foreach (string file in Directory.EnumerateFiles(sourceDirectory))
        {
            string targetFile = Path.Combine(targetDirectory, Path.GetFileName(file));
            File.Copy(file, targetFile);
        }
    }
}

public sealed record IsolatedInput(
    string OriginalRootPath,
    string IsolatedRootPath) : IDisposable
{
    public void Dispose()
    {
        if (Directory.Exists(IsolatedRootPath))
        {
            Directory.Delete(IsolatedRootPath, recursive: true);
        }
    }
}
