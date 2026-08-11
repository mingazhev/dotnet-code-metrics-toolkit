using System.Security.Cryptography;
using System.Text;

namespace CodeMetricsToolkit.Core.Discovery;

internal static class ProjectIdentity
{
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
