namespace CodeMetricsToolkit.Core.Facts;

internal sealed record ProjectDependencyFacts(
    string FromProjectPath,
    string ToProjectPath);
