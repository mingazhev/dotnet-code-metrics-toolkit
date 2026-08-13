namespace CodeMetricsToolkit.Core.Discovery;

public static class SourceFileDiscovery
{
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

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

    private static readonly string[] GeneratedFileSuffixes =
    [
        ".g.cs",
        ".generated.cs",
        ".designer.cs"
    ];

    public static DiscoveredSources Discover(
        string inputPath,
        bool includeGeneratedCode,
        IReadOnlyList<string>? includePatterns = null,
        IReadOnlyList<string>? excludePatterns = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        InputSelection selection = ResolveInputSelection(inputPath);
        var rootPath = selection.RootPath;
        var rootDirectory = new DirectoryInfo(rootPath);
        includePatterns ??= [];
        excludePatterns ??= [];

        if (!rootDirectory.Exists)
        {
            throw new DirectoryNotFoundException($"Input path does not exist: {inputPath}");
        }

        var solutionPaths = EnumerateFiles(
                rootDirectory,
                "*.sln",
                includeGeneratedCode: true,
                cancellationToken)
            .Select(file => ToRelativePath(rootPath, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        var discoveredProjectPaths = EnumerateFiles(
                rootDirectory,
                "*.csproj",
                includeGeneratedCode: true,
                cancellationToken)
            .Select(file => ToRelativePath(rootPath, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        List<string> projectPaths = SelectProjectPaths(
            rootPath,
            selection,
            discoveredProjectPaths);
        IReadOnlyList<string> sourceProjectPaths = projectPaths.Count == 0
            ? [ProjectIdentity.SyntheticProjectPath]
            : projectPaths;

        var candidateSourceFiles = EnumerateFiles(
                rootDirectory,
                "*.cs",
                includeGeneratedCode,
                cancellationToken)
            .Where(file => ShouldIncludeSourceFile(rootPath, file.FullName, includePatterns, excludePatterns))
            .Select(file => CreateSourceFile(rootPath, sourceProjectPaths, file.FullName))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();
        var sourceFiles = candidateSourceFiles
            .Where(file => IsInSelectedProjectScope(file, selection, sourceProjectPaths))
            .Select(file => ReassignSelectedProject(file, selection))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToList();

        return new DiscoveredSources(
            rootPath,
            solutionPaths,
            projectPaths.Count == 0 && sourceFiles.Count > 0
                ? [ProjectIdentity.SyntheticProjectPath]
                : projectPaths,
            sourceFiles,
            selection.SelectedSolutionPath,
            selection.SelectedProjectPath)
        {
            CandidateSourceFiles = candidateSourceFiles
        };
    }

    internal static InputSelection ResolveInputSelection(string inputPath)
    {
        var fullPath = Path.GetFullPath(inputPath);

        if (File.Exists(fullPath))
        {
            var rootPath = Path.GetDirectoryName(fullPath) ??
                throw new DirectoryNotFoundException($"Could not resolve directory for {inputPath}");
            var relativePath = Path.GetFileName(fullPath);

            if (fullPath.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                List<string> referencedProjects = ReadSolutionProjectFullPaths(fullPath);
                var solutionRoot = FindCommonSolutionRoot(rootPath, referencedProjects);
                var selectedSolutionPath = ToRelativePath(solutionRoot, fullPath);

                return new InputSelection(solutionRoot, selectedSolutionPath, null);
            }

            if (fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return new InputSelection(rootPath, null, relativePath);
            }

            throw new ArgumentException(
                "Input file must be a .sln or .csproj file; use its containing directory for a source tree.",
                nameof(inputPath));
        }

        return new InputSelection(fullPath, null, null);
    }

    private static List<string> SelectProjectPaths(
        string rootPath,
        InputSelection selection,
        IReadOnlyList<string> discoveredProjectPaths)
    {
        if (selection.SelectedProjectPath is not null)
        {
            return discoveredProjectPaths
                .Where(path => string.Equals(
                    path,
                    selection.SelectedProjectPath,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
                .ToList();
        }

        if (selection.SelectedSolutionPath is null)
        {
            return discoveredProjectPaths.ToList();
        }

        HashSet<string> solutionProjects = ReadSolutionProjectPaths(
            rootPath,
            selection.SelectedSolutionPath);

        return discoveredProjectPaths
            .Where(solutionProjects.Contains)
            .ToList();
    }

    private static HashSet<string> ReadSolutionProjectPaths(
        string rootPath,
        string solutionPath)
    {
        var fullSolutionPath = Path.Combine(rootPath, solutionPath);
        var projects = new HashSet<string>(PathComparer);

        foreach (var projectFullPath in ReadSolutionProjectFullPaths(fullSolutionPath))
        {
            var relativePath = Path.GetRelativePath(rootPath, projectFullPath)
                .Replace(Path.DirectorySeparatorChar, '/');

            if (!string.Equals(relativePath, "..", StringComparison.Ordinal) &&
                !relativePath.StartsWith("../", StringComparison.Ordinal))
            {
                projects.Add(relativePath);
            }
        }

        return projects;
    }

    private static List<string> ReadSolutionProjectFullPaths(string fullSolutionPath)
    {
        var projectPathPattern = new System.Text.RegularExpressions.Regex(
            "^Project\\([^)]*\\)\\s*=\\s*\"[^\"]*\",\\s*\"(?<path>[^\"]+\\.csproj)\"",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant |
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var projects = new List<string>();

        foreach (var line in File.ReadLines(fullSolutionPath))
        {
            System.Text.RegularExpressions.Match match = projectPathPattern.Match(line);
            if (match.Success)
            {
                projects.Add(Path.GetFullPath(
                    match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar),
                    Path.GetDirectoryName(fullSolutionPath)!));
            }
        }

        return projects;
    }

    private static string FindCommonSolutionRoot(
        string solutionDirectory,
        List<string> projectPaths)
    {
        var commonRoot = Path.GetFullPath(solutionDirectory);

        foreach (var projectPath in projectPaths)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath)!;

            while (!IsWithinDirectory(commonRoot, projectDirectory))
            {
                DirectoryInfo? parent = Directory.GetParent(commonRoot);
                if (parent is null)
                {
                    throw new ArgumentException(
                        "The solution references projects without a safe common analysis root.",
                        nameof(projectPaths));
                }

                commonRoot = parent.FullName;
            }
        }

        var filesystemRoot = Path.GetPathRoot(commonRoot)!;
        if (projectPaths.Count > 0 &&
            PathComparer.Equals(
                Path.TrimEndingDirectorySeparator(commonRoot),
                Path.TrimEndingDirectorySeparator(filesystemRoot)))
        {
            throw new ArgumentException(
                "The solution references projects whose only common analysis root is the filesystem root.",
                nameof(projectPaths));
        }

        if (!PathComparer.Equals(
                Path.TrimEndingDirectorySeparator(commonRoot),
                Path.TrimEndingDirectorySeparator(solutionDirectory)))
        {
            var repositoryBoundary = FindRepositoryBoundary(solutionDirectory);

            if (repositoryBoundary is null || !IsWithinDirectory(repositoryBoundary, commonRoot))
            {
                throw new ArgumentException(
                    "The solution references projects outside its directory without a recognized " +
                    "repository boundary (.git, global.json, or Directory.Build.props). " +
                    "Analyze the repository root directory instead.",
                    nameof(projectPaths));
            }
        }

        return commonRoot;
    }

    private static string? FindRepositoryBoundary(string startDirectory)
    {
        for (DirectoryInfo? directory = new(startDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, "global.json")) ||
                File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private static bool IsWithinDirectory(string rootPath, string candidatePath)
    {
        var relativePath = Path.GetRelativePath(rootPath, candidatePath)
            .Replace(Path.DirectorySeparatorChar, '/');

        return !string.Equals(relativePath, "..", StringComparison.Ordinal) &&
            !relativePath.StartsWith("../", StringComparison.Ordinal) &&
            !Path.IsPathRooted(relativePath);
    }

    private static bool IsInSelectedProjectScope(
        DiscoveredSourceFile sourceFile,
        InputSelection selection,
        IReadOnlyCollection<string> selectedProjectPaths)
    {
        if (selection.SelectedSolutionPath is null && selection.SelectedProjectPath is null)
        {
            return true;
        }

        if (selectedProjectPaths.Contains(sourceFile.ProjectPath, PathComparer))
        {
            return true;
        }

        return selection.SelectedProjectPath is not null &&
            string.Equals(
                Path.GetDirectoryName(sourceFile.ProjectPath),
                Path.GetDirectoryName(selection.SelectedProjectPath),
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
    }

    private static DiscoveredSourceFile ReassignSelectedProject(
        DiscoveredSourceFile sourceFile,
        InputSelection selection)
    {
        if (selection.SelectedProjectPath is null ||
            PathComparer.Equals(sourceFile.ProjectPath, selection.SelectedProjectPath))
        {
            return sourceFile;
        }

        return sourceFile with
        {
            ProjectPath = selection.SelectedProjectPath,
            ProjectKey = ProjectIdentity.Key(selection.SelectedProjectPath)
        };
    }

    private static IEnumerable<FileInfo> EnumerateFiles(
        DirectoryInfo rootDirectory,
        string searchPattern,
        bool includeGeneratedCode,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<DirectoryInfo>();
        pending.Push(rootDirectory);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryInfo directory = pending.Pop();

            foreach (DirectoryInfo childDirectory in directory.EnumerateDirectories("*", EnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (ShouldSkipDirectory(childDirectory))
                {
                    continue;
                }

                pending.Push(childDirectory);
            }

            foreach (FileInfo file in directory.EnumerateFiles(searchPattern, EnumerationOptions))
            {
                cancellationToken.ThrowIfCancellationRequested();

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

    private static bool ShouldIncludeSourceFile(
        string rootPath,
        string fullPath,
        IReadOnlyList<string> includePatterns,
        IReadOnlyList<string> excludePatterns)
    {
        var relativePath = ToRelativePath(rootPath, fullPath);

        var included = includePatterns.Count == 0 ||
            includePatterns.Any(pattern => GlobMatches(pattern, relativePath));
        var excluded = excludePatterns.Any(pattern => GlobMatches(pattern, relativePath));

        return included && !excluded;
    }

    internal static bool GlobMatches(string pattern, string relativePath)
    {
        var normalizedPattern = NormalizeGlobValue(pattern);
        var normalizedPath = NormalizeGlobValue(relativePath);

        return GlobRegex(normalizedPattern).IsMatch(normalizedPath);
    }

    private static System.Text.RegularExpressions.Regex GlobRegex(string pattern)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append('^');

        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];

            if (current == '*' &&
                index + 2 < pattern.Length &&
                pattern[index + 1] == '*' &&
                pattern[index + 2] == '/')
            {
                builder.Append("(?:.*/)?");
                index += 2;
                continue;
            }

            if (current == '*')
            {
                var isDoubleStar = index + 1 < pattern.Length && pattern[index + 1] == '*';
                builder.Append(isDoubleStar ? ".*" : "[^/]*");

                if (isDoubleStar)
                {
                    index++;
                }

                continue;
            }

            if (current == '?')
            {
                builder.Append("[^/]");
                continue;
            }

            builder.Append(System.Text.RegularExpressions.Regex.Escape(current.ToString()));
        }

        builder.Append('$');

        return new System.Text.RegularExpressions.Regex(builder.ToString(), System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    }

    private static string NormalizeGlobValue(string value)
    {
        var normalized = value.Replace('\\', '/');

        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    private static DiscoveredSourceFile CreateSourceFile(
        string rootPath,
        IReadOnlyList<string> projectPaths,
        string filePath)
    {
        var relativePath = ToRelativePath(rootPath, filePath);
        var projectPath = FindNearestProjectPath(relativePath, projectPaths) ?? rootPath;

        return new DiscoveredSourceFile(
            filePath,
            relativePath,
            projectPath,
            ProjectIdentity.Key(projectPath));
    }

    private static string? FindNearestProjectPath(string relativeFilePath, IReadOnlyList<string> projectPaths)
    {
        string? bestProjectPath = null;
        var bestLength = -1;

        foreach (var projectPath in projectPaths)
        {
            var projectDirectory = Path.GetDirectoryName(projectPath);
            var normalizedDirectory = string.IsNullOrEmpty(projectDirectory)
                ? string.Empty
                : projectDirectory.Replace(Path.DirectorySeparatorChar, '/');

            var isMatch = normalizedDirectory.Length == 0 ||
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

}

public sealed record DiscoveredSources(
    string RootPath,
    IReadOnlyList<string> SolutionPaths,
    IReadOnlyList<string> ProjectPaths,
    IReadOnlyList<DiscoveredSourceFile> SourceFiles,
    string? SelectedSolutionPath,
    string? SelectedProjectPath)
{
    // Selected solution/project scope is finalized from evaluated MSBuild Compile items.
    // These candidates preserve in-root linked sources that directory proximity cannot identify.
    internal IReadOnlyList<DiscoveredSourceFile> CandidateSourceFiles { get; init; } = SourceFiles;
}

public sealed record DiscoveredSourceFile(
    string FullPath,
    string RelativePath,
    string ProjectPath,
    string ProjectKey);

internal sealed record InputSelection(
    string RootPath,
    string? SelectedSolutionPath,
    string? SelectedProjectPath);
