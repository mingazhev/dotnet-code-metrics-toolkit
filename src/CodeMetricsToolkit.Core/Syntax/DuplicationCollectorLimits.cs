namespace CodeMetricsToolkit.Core.Syntax;

internal sealed record DuplicationCollectorLimits(
    int MaxTokens,
    int MaxWindows,
    long MaxWorkUnits)
{
    public static DuplicationCollectorLimits Default { get; } = new(
        MaxTokens: 500_000,
        MaxWindows: 500_000,
        MaxWorkUnits: 20_000_000);
}
