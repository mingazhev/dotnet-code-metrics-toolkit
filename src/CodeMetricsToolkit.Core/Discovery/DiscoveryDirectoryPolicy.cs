namespace CodeMetricsToolkit.Core.Discovery;

internal static class DiscoveryDirectoryPolicy
{
    private static readonly string[] ExcludedNames = [".git", ".vs", "bin", "obj", "artifacts"];

    public static bool IsExcluded(string directoryName)
    {
        return ExcludedNames.Contains(directoryName, StringComparer.OrdinalIgnoreCase);
    }
}
