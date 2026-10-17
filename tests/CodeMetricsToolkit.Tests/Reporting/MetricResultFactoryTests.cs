using CodeMetricsToolkit.Core.Reporting;

namespace CodeMetricsToolkit.Tests.Reporting;

public sealed class MetricResultFactoryTests
{
    [Fact]
    public void FactoryUsesCatalogMetadataAndValidatesTargetKind()
    {
        MetricResultLine ratio = MetricResultFactory.Number(
            "token_line_ratio",
            "file",
            "file:a",
            "semantic",
            "A.cs",
            1,
            1,
            1.0);

        Assert.Equal("1.0.0", ratio.MetricVersion);
        Assert.Equal("ratio", ratio.Unit);
        Assert.Equal("number", ratio.ValueKind);
        MetricResultLine count = MetricResultFactory.Numeric(
            "type_count",
            "file",
            "file:a",
            "semantic",
            "A.cs",
            1,
            1,
            1);
        Assert.Equal("count", count.Unit);
        Assert.Equal("integer", count.ValueKind);
        Assert.Throws<ArgumentException>(() => MetricResultFactory.Numeric(
            "type_count",
            "member",
            "member:a",
            "semantic",
            "A.cs",
            1,
            1,
            1));
    }
}
