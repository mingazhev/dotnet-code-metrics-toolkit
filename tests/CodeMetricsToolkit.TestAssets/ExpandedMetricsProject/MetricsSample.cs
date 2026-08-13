namespace ExpandedMetricsProject;

/// <summary>Base API.</summary>
public class BaseApi
{
}

/// <summary>Exercises semantic and control-flow metrics.</summary>
public sealed class MetricsSample : BaseApi
{
    private readonly Dependency dependency = new();

    /// <summary>Computes a value asynchronously.</summary>
    public async Task<int> ComputeAsync(int input)
    {
        object allocation = new();
        await Task.Yield();

        return input > 0
            ? dependency.Value() + allocation.GetHashCode()
            : 0;
    }

    public int A(int value)
    {
        return value <= 0 ? 0 : B(value - 1);
    }

    public int B(int value)
    {
        return value <= 0 ? 0 : A(value - 1);
    }

    private int Self(int value)
    {
        return value <= 0 ? 0 : Self(value - 1);
    }
}

internal sealed class Dependency
{
    public int Value() => 1;
}
