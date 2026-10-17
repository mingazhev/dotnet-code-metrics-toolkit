using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Metrics;

namespace CodeMetricsToolkit.Tests.Metrics;

public sealed class MetricResultFactoryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(0.5)]
    public void RatioValueKindDoesNotDependOnItsValue(double value)
    {
        MetricResultLine ratio = MetricResultFactory.Create(
            "token_line_ratio", "file", "file:a", "syntax_fallback", "A.cs", 1, 1, value);

        Assert.Equal("number", ratio.ValueKind);
        Assert.Equal(value, ratio.NumericValue);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void CountRejectsValuesOutsideItsIntegerContract(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MetricResultFactory.Create(
            "type_count", "file", "file:a", "syntax_fallback", "A.cs", 1, 1, value));
    }

    [Fact]
    public void FactoryUsesCatalogMetadataAndValidatesTargetKind()
    {
        MetricResultLine ratio = MetricResultFactory.Create(
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
        MetricResultLine count = MetricResultFactory.Create(
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
        Assert.Throws<ArgumentException>(() => MetricResultFactory.Create(
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
