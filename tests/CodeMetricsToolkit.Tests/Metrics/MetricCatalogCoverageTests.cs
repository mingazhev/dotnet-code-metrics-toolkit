using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Tests.Support;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class MetricCatalogCoverageTests
{
    [Fact]
    public void EveryCatalogMetricBelongsToAnExplicitlyUnitTestedFamily()
    {
        var catalogIds = MetricCatalog.All
            .Select(metric => metric.Id)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Equal(catalogIds.Order(StringComparer.Ordinal), MetricFamilies.All.Order(StringComparer.Ordinal));
        Assert.Equal(MetricCatalog.All.Count, catalogIds.Count);
        Assert.All(
            MetricCatalog.All,
            metric => Assert.False(string.IsNullOrWhiteSpace(metric.Formula)));
    }
}
