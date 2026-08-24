namespace CodeMetricsToolkit.Core.Syntax;

internal static class DotnetMuxer
{
    public static string FileName { get; } = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";

    public static string Resolve()
    {
        if (TryUseRootedExisting(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH"), out var hostPath))
        {
            return hostPath;
        }

        var processPath = Environment.ProcessPath;
        if (processPath is not null &&
            string.Equals(
                Path.GetFileName(processPath),
                FileName,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) &&
            TryUseRootedExisting(processPath, out var processMuxer))
        {
            return processMuxer;
        }

        if (TryUseRootedExisting(
                CombineIfPresent(Environment.GetEnvironmentVariable("DOTNET_ROOT"), FileName),
                out var rootMuxer))
        {
            return rootMuxer;
        }

        if (TryUseRootedExisting(ResolveFromRuntimeDirectory(), out var runtimeMuxer))
        {
            return runtimeMuxer;
        }

        throw new InvalidOperationException(
            "Could not resolve an absolute path to the dotnet host. Set DOTNET_HOST_PATH or DOTNET_ROOT.");
    }

    private static string? ResolveFromRuntimeDirectory()
    {
        var coreLib = typeof(object).Assembly.Location;
        if (string.IsNullOrWhiteSpace(coreLib))
        {
            return null;
        }

        var versionDirectory = Path.GetDirectoryName(coreLib);
        var frameworkDirectory = Path.GetDirectoryName(versionDirectory);
        var sharedDirectory = Path.GetDirectoryName(frameworkDirectory);
        var root = Path.GetDirectoryName(sharedDirectory);
        return root is null ? null : Path.Combine(root, FileName);
    }

    private static string? CombineIfPresent(string? directory, string fileName)
    {
        return string.IsNullOrWhiteSpace(directory) ? null : Path.Combine(directory, fileName);
    }

    private static bool TryUseRootedExisting(string? path, out string rootedPath)
    {
        rootedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathRooted(path) || !File.Exists(path))
        {
            return false;
        }

        rootedPath = Path.GetFullPath(path);
        return Path.IsPathRooted(rootedPath);
    }
}
