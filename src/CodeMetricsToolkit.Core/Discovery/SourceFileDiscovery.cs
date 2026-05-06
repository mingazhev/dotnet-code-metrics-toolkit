namespace CodeMetricsToolkit.Core.Discovery;

public static class SourceFileDiscovery
{
    private static readonly string[] ExcludedDirectoryNames =
    [
        ".git",
        ".vs",
        "bin",
        "obj",
        "artifacts"
    ];

    private static readonly string[] GeneratedFileSuffixes =
    [
        ".g.cs",
        ".generated.cs",
        ".designer.cs"
    ];

    public static DiscoveredSources Discover(string inputPath, bool includeGeneratedCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        string rootPath = ResolveRootPath(inputPath);
        var rootDirectory = new DirectoryInfo(rootPath);

        if (!rootDirectory.Exists)
        {
            throw new DirectoryNotFoundException($"Input path does not exist: {inputPath}");
        }

        List<string> projectPaths = EnumerateFiles(rootDirectory, "*.csproj", includeGeneratedCode: true)
            .Select(file => ToRelativePath(rootPath, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        List<DiscoveredSourceFile> sourceFiles = EnumerateFiles(rootDirectory, "*.cs", includeGeneratedCode)
            .Select(file => CreateSourceFile(rootPath, projectPaths, file.FullName))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        return new DiscoveredSources(rootPath, projectPaths, sourceFiles);
    }

    private static string ResolveRootPath(string inputPath)
    {
        string fullPath = Path.GetFullPath(inputPath);

        if (File.Exists(fullPath))
        {
            return Path.GetDirectoryName(fullPath) ??
                throw new DirectoryNotFoundException($"Could not resolve directory for {inputPath}");
        }

        return fullPath;
    }

    private static IEnumerable<FileInfo> EnumerateFiles(
        DirectoryInfo rootDirectory,
        string searchPattern,
        bool includeGeneratedCode)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            DirectoryInfo directory = pending.Pop();

            foreach (DirectoryInfo childDirectory in directory.EnumerateDirectories())
            {
                if (ShouldSkipDirectory(childDirectory))
                {
                    continue;
                }

                pending.Push(childDirectory);
            }

            foreach (FileInfo file in directory.EnumerateFiles(searchPattern))
            {
                if (!includeGeneratedCode && IsGeneratedFile(file.Name))
                {
                    continue;
                }

                yield return file;
            }
        }
    }

    private static bool ShouldSkipDirectory(DirectoryInfo directory)
    {
        return ExcludedDirectoryNames.Contains(directory.Name, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedFile(string fileName)
    {
        return GeneratedFileSuffixes.Any(suffix => fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(fileName, "AssemblyInfo.cs", StringComparison.OrdinalIgnoreCase);
    }

    private static DiscoveredSourceFile CreateSourceFile(
        string rootPath,
        IReadOnlyList<string> projectPaths,
        string filePath)
    {
        string relativePath = ToRelativePath(rootPath, filePath);
        string projectPath = FindNearestProjectPath(relativePath, projectPaths) ?? rootPath;

        return new DiscoveredSourceFile(
            filePath,
            relativePath,
            projectPath,
            StableHash(projectPath));
    }

    private static string? FindNearestProjectPath(string relativeFilePath, IReadOnlyList<string> projectPaths)
    {
        string? bestProjectPath = null;
        int bestLength = -1;

        foreach (string projectPath in projectPaths)
        {
            string? projectDirectory = Path.GetDirectoryName(projectPath);
            string normalizedDirectory = string.IsNullOrEmpty(projectDirectory)
                ? string.Empty
                : projectDirectory.Replace(Path.DirectorySeparatorChar, '/');

            bool isMatch = normalizedDirectory.Length == 0 ||
                relativeFilePath.StartsWith(normalizedDirectory + "/", StringComparison.Ordinal);

            if (isMatch && normalizedDirectory.Length > bestLength)
            {
                bestProjectPath = projectPath;
                bestLength = normalizedDirectory.Length;
            }
        }

        return bestProjectPath;
    }

    private static string ToRelativePath(string rootPath, string filePath)
    {
        return Path.GetRelativePath(rootPath, filePath)
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string StableHash(string value)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
        byte[] hash = System.Security.Cryptography.SHA256.HashData(bytes);

        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }
}

public sealed record DiscoveredSources(
    string RootPath,
    IReadOnlyList<string> ProjectPaths,
    IReadOnlyList<DiscoveredSourceFile> SourceFiles);

public sealed record DiscoveredSourceFile(
    string FullPath,
    string RelativePath,
    string ProjectPath,
    string ProjectKey);
