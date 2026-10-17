namespace CodeMetricsToolkit.Core.Facts;

internal sealed record DuplicationFacts(IReadOnlyList<DuplicationFileFacts> Files);

internal sealed record DuplicationFileFacts(
    string FilePath,
    IReadOnlyList<DuplicationRegionFacts> Regions,
    int DuplicatedLineCount);

internal sealed record DuplicationRegionFacts(int StartLine, int EndLine, string Tag);
