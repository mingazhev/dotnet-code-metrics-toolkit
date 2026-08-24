namespace CodeMetricsToolkit.Core.Facts;

public sealed record TypeSemanticFacts
{
    public required int InheritanceDepth { get; init; }
    public required int ClassCoupling { get; init; }
    public required int PublicApiCount { get; init; }
    public required int DocumentedPublicApiCount { get; init; }
}
