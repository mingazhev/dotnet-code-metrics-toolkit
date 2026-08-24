namespace CodeMetricsToolkit.Core.Discovery;

public static class InputIsolator
{
    internal const long MaxIsolatedInputBytes = 4L * 1024 * 1024 * 1024;
    internal const long MaxIsolatedFileBytes = 256L * 1024 * 1024;
    internal const int MaxIsolatedFileCount = 250_000;
    internal const int MaxIsolatedDirectoryCount = 50_000;
    internal const int MaxIsolatedDepth = 96;

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
        return CopyToTemporaryDirectory(
            inputPath,
            InputIsolationLimits.Default,
            cancellationToken);
    }

    internal static IsolatedInput CopyToTemporaryDirectory(
        string inputPath,
        InputIsolationLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        InputSelection selection = SourceFileDiscovery.ResolveInputSelection(inputPath);
        var sourceRoot = selection.RootPath;
        var selectedRelativePath = selection.SelectedSolutionPath ?? selection.SelectedProjectPath;
        var tempRoot = Path.Combine(Path.GetTempPath(), "codemetrics-input-" + Guid.NewGuid().ToString("N"));

        try
        {
            CreatePrivateDirectory(tempRoot);
            var state = new IsolationCopyState(sourceRoot);
            CopyDirectory(sourceRoot, tempRoot, depth: 0, limits, state, cancellationToken);
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
        int depth,
        InputIsolationLimits limits,
        IsolationCopyState state,
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

            var childDepth = depth + 1;
            if (childDepth > limits.MaxDepth)
            {
                throw LimitExceeded(
                    state,
                    directory,
                    $"directory depth exceeds {limits.MaxDepth}");
            }

            if (state.DirectoryCount >= limits.MaxDirectoryCount)
            {
                throw LimitExceeded(
                    state,
                    directory,
                    $"directory count exceeds {limits.MaxDirectoryCount}");
            }

            state.DirectoryCount++;
            var targetChildDirectory = Path.Combine(targetDirectory, directoryName);
            CreatePrivateDirectory(targetChildDirectory);
            CopyDirectory(
                directory,
                targetChildDirectory,
                childDepth,
                limits,
                state,
                cancellationToken);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", EnumerationOptions))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!FileKind.IsRegularFile(file))
            {
                throw LimitExceeded(
                    state,
                    file,
                    "path is not a regular file");
            }

            CopyFile(
                file,
                Path.Combine(targetDirectory, Path.GetFileName(file)),
                limits,
                state,
                cancellationToken);
        }
    }

    private static void CopyFile(
        string sourceFile,
        string targetFile,
        InputIsolationLimits limits,
        IsolationCopyState state,
        CancellationToken cancellationToken)
    {
        var declaredLength = new FileInfo(sourceFile).Length;
        if (declaredLength > limits.MaxFileBytes)
        {
            throw LimitExceeded(
                state,
                sourceFile,
                $"file size {declaredLength} bytes exceeds {limits.MaxFileBytes} bytes");
        }

        if (state.FileCount >= limits.MaxFileCount)
        {
            throw LimitExceeded(
                state,
                sourceFile,
                $"file count exceeds {limits.MaxFileCount}");
        }

        if (declaredLength > limits.MaxTotalBytes - state.TotalBytes)
        {
            throw LimitExceeded(
                state,
                sourceFile,
                $"total size exceeds {limits.MaxTotalBytes} bytes");
        }

        state.FileCount++;
        using var source = new FileStream(
            sourceFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var target = new FileStream(
            targetFile,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var buffer = new byte[64 * 1024];
        long fileBytes = 0;
        int bytesRead;

        while ((bytesRead = source.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (bytesRead > limits.MaxFileBytes - fileBytes)
            {
                throw LimitExceeded(
                    state,
                    sourceFile,
                    $"file grew beyond {limits.MaxFileBytes} bytes while it was copied");
            }

            if (bytesRead > limits.MaxTotalBytes - state.TotalBytes)
            {
                throw LimitExceeded(
                    state,
                    sourceFile,
                    $"total size exceeds {limits.MaxTotalBytes} bytes");
            }

            target.Write(buffer, 0, bytesRead);
            fileBytes += bytesRead;
            state.TotalBytes += bytesRead;
        }
    }

    private static InvalidDataException LimitExceeded(
        IsolationCopyState state,
        string path,
        string detail)
    {
        var relativePath = Path.GetRelativePath(state.SourceRoot, path)
            .Replace(Path.DirectorySeparatorChar, '/');

        return new InvalidDataException(
            $"Input isolation limit exceeded at '{relativePath}': {detail}.");
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

    private sealed class IsolationCopyState
    {
        public IsolationCopyState(string sourceRoot)
        {
            SourceRoot = sourceRoot;
        }

        public string SourceRoot { get; }
        public long TotalBytes { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; } = 1;
    }
}

internal sealed record InputIsolationLimits(
    long MaxTotalBytes,
    long MaxFileBytes,
    int MaxFileCount,
    int MaxDirectoryCount,
    int MaxDepth)
{
    public static InputIsolationLimits Default { get; } = new(
        InputIsolator.MaxIsolatedInputBytes,
        InputIsolator.MaxIsolatedFileBytes,
        InputIsolator.MaxIsolatedFileCount,
        InputIsolator.MaxIsolatedDirectoryCount,
        InputIsolator.MaxIsolatedDepth);

    public void Validate()
    {
        if (MaxTotalBytes <= 0 ||
            MaxFileBytes <= 0 ||
            MaxFileCount <= 0 ||
            MaxDirectoryCount <= 0 ||
            MaxDepth < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(InputIsolationLimits),
                "Input isolation limits must be positive; maximum depth may be zero.");
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
