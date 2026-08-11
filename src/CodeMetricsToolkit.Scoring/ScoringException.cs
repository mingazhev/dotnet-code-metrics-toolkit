namespace CodeMetricsToolkit.Scoring;

public sealed class ScoringException : Exception
{
    public ScoringException(ScoringFailureKind failureKind, string message)
        : base(message)
    {
        FailureKind = failureKind;
    }

    public ScoringException(ScoringFailureKind failureKind, string message, Exception innerException)
        : base(message, innerException)
    {
        FailureKind = failureKind;
    }

    public ScoringFailureKind FailureKind { get; }
}
