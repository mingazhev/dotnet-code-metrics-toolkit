namespace CodeMetricsToolkit.Cli;

public static class CliExitCodes
{
    public const int Success = 0;
    public const int InputError = 1;
    public const int InvalidArtifacts = 2;
    public const int AnalysisRejected = 3;
    public const int InternalError = 70;
    public const int Canceled = 130;
}
