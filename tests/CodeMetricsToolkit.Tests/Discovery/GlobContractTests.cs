using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Scoring;

namespace CodeMetricsToolkit.Tests.Discovery;

public sealed class GlobContractTests
{
    [Theory]
    [InlineData("src/**/*.cs", "src/A.cs", true)]
    [InlineData("src/**/*.cs", "src/Nested/A.cs", true)]
    [InlineData("*.cs", "A.cs", true)]
    [InlineData("*.cs", "src/A.cs", false)]
    [InlineData("./src/*.cs", "src/A.cs", true)]
    [InlineData("src/?ample.cs", "src/Sample.cs", true)]
    [InlineData("src/*.cs", "src/Nested/A.cs", false)]
    [InlineData("**/Generated/*.cs", "Generated/A.cs", true)]
    [InlineData("**/Generated/*.cs", "src/Generated/A.cs", true)]
    public void DiscoveryAndScoringUseTheSamePathGlobContract(
        string pattern,
        string path,
        bool expected)
    {
        Assert.Equal(expected, SourceFileDiscovery.GlobMatches(pattern, path));
        Assert.Equal(expected, GlobMatcher.IsMatch(pattern, path));
    }
}
