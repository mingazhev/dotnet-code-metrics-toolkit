using CodeMetricsToolkit.Core.Syntax;

namespace CodeMetricsToolkit.Tests.Syntax;

public sealed class DotnetMuxerTests
{
    [Fact]
    public void ResolveReturnsARootedExistingMuxer()
    {
        var path = DotnetMuxer.Resolve();

        Assert.True(Path.IsPathRooted(path));
        Assert.True(File.Exists(path));
        Assert.Equal(
            Path.GetFileNameWithoutExtension(DotnetMuxer.FileName),
            Path.GetFileNameWithoutExtension(path),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveIgnoresARelativeHostPathEnvironmentVariable()
    {
        var previous = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", "dotnet");

            var path = DotnetMuxer.Resolve();

            Assert.True(Path.IsPathRooted(path));
            Assert.NotEqual("dotnet", path, StringComparer.Ordinal);
            Assert.NotEqual(Path.Combine(Directory.GetCurrentDirectory(), "dotnet"), path);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", previous);
        }
    }

    [Fact]
    public void ResolvePrefersARootedHostPathEnvironmentVariable()
    {
        var resolved = DotnetMuxer.Resolve();
        var previous = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", resolved);

            Assert.Equal(Path.GetFullPath(resolved), DotnetMuxer.Resolve());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_HOST_PATH", previous);
        }
    }
}
