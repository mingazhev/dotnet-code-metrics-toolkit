using System.Reflection;

namespace CodeMetricsToolkit.Core;

public static class ToolkitInfo
{
    public const string Name = "CodeMetricsToolkit";

    public static string Version { get; } = ResolveVersion();

    private static string ResolveVersion()
    {
        Assembly assembly = typeof(ToolkitInfo).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }
}
