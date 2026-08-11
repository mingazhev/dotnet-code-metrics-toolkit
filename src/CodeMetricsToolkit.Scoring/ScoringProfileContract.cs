namespace CodeMetricsToolkit.Scoring;

/// <summary>Versions accepted by the strict scoring-profile reader.</summary>
public static class ScoringProfileContract
{
    /// <summary>The JSON shape of a scoring profile.</summary>
    public const string Current = "1.0.0";

    /// <summary>
    /// The CodeMetricsToolkit artifact contract that every profile must declare and every input
    /// artifact must match exactly. Scoring never guesses compatibility across contract versions.
    /// </summary>
    public const string SupportedArtifactContractVersion = "0.1.0";
}
