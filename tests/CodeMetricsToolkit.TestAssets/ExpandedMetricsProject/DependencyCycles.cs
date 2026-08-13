namespace ExpandedMetricsProject;

internal sealed class CycleA
{
    public CycleB Next(CycleB value) => value;
}

internal sealed class CycleB
{
    public CycleA Next(CycleA value) => value;
}
