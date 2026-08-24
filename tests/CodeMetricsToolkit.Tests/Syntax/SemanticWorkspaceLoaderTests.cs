using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class SemanticWorkspaceLoaderTests
{
    [Fact]
    public void OperationalIoFailuresAreDegradedRatherThanInternalErrors()
    {
        Assert.True(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new IOException("disk")));
        Assert.True(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new UnauthorizedAccessException()));
        Assert.True(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new InvalidOperationException("workspace")));
    }

    [Fact]
    public void ContractAndLifetimeFailuresAreNotSwallowedAsDegradedWorkspaceLoad()
    {
        Assert.False(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new ArgumentException("contract")));
        Assert.False(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new ArgumentNullException("value")));
        Assert.False(SemanticWorkspaceLoader.IsOperationalWorkspaceException(new ObjectDisposedException("workspace")));
    }
}
