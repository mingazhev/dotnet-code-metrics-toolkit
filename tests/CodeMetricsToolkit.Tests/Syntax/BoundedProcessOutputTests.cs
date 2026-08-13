using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class BoundedProcessOutputTests
{
    [Fact]
    public void BufferKeepsSmallOutputUnchanged()
    {
        var buffer = new BoundedProcessOutput();

        buffer.Append("first");
        buffer.Append("second");

        Assert.Equal("first\nsecond", buffer.ToString());
        Assert.False(buffer.WasTruncated);
    }

    [Fact]
    public void BufferStopsRetainingUnboundedProcessOutput()
    {
        var buffer = new BoundedProcessOutput();

        for (var index = 0; index < 10_000; index++)
        {
            buffer.Append(new string('x', 1_000));
        }

        Assert.True(buffer.WasTruncated);
        Assert.True(buffer.ToString().Length < 17_000);
        Assert.EndsWith("[process output truncated]", buffer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ChunkBufferCapsAContinuousStreamWithoutLineBoundaries()
    {
        var buffer = new BoundedProcessOutput();
        ReadOnlySpan<char> chunk = new string('x', 4_096).AsSpan();

        for (var index = 0; index < 100; index++)
        {
            buffer.AppendChunk(chunk);
        }

        Assert.True(buffer.WasTruncated);
        Assert.True(buffer.ToString().Length < 17_000);
        Assert.EndsWith("[process output truncated]", buffer.ToString(), StringComparison.Ordinal);
    }
}
