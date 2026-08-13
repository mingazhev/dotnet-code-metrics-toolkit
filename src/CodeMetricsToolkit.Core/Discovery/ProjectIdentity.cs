using System.Security.Cryptography;
using System.Text;

namespace CodeMetricsToolkit.Core.Discovery;

internal static class ProjectIdentity
{
    public const string SyntheticProjectPath = ".";
    public const string SyntheticProjectName = "source-tree";

    public static bool IsSynthetic(string projectPath)
    {
        return string.Equals(projectPath, SyntheticProjectPath, StringComparison.Ordinal);
    }

    public static string Key(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var bytes = Encoding.UTF8.GetBytes(projectPath);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    public static string TargetId(string projectPath)
    {
        return $"project:{Key(projectPath)}";
    }
}
