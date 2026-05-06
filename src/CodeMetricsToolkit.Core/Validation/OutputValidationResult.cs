namespace CodeMetricsToolkit.Core.Validation;

public sealed record OutputValidationResult
{
    public required bool IsValid { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }
}
