using System.Runtime.InteropServices;
using CodeMetricsToolkit.Core.Discovery;

namespace CodeMetricsToolkit.Tests.Discovery;

public sealed class FileKindTests
{
    [Fact]
    public void RegularFileIsAccepted()
    {
        using var fixture = new TemporaryDirectory();
        var path = Path.Combine(fixture.Path, "Regular.cs");
        File.WriteAllText(path, "internal sealed class Regular { }");

        Assert.True(FileKind.IsRegularFile(path));
    }

    [Fact]
    public void SymbolicLinkIsNotARegularFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryDirectory();
        var target = Path.Combine(fixture.Path, "Target.cs");
        var link = Path.Combine(fixture.Path, "Link.cs");
        File.WriteAllText(target, "internal sealed class Target { }");
        File.CreateSymbolicLink(link, target);

        Assert.False(FileKind.IsRegularFile(link));
    }

    [Fact]
    public void FifoIsNotARegularFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryDirectory();
        var path = Path.Combine(fixture.Path, "NamedPipe.cs");
        Assert.Equal(0, MkFifo(path, Convert.ToInt32("644", 8)));

        Assert.False(FileKind.IsRegularFile(path));
        InvalidDataException exception = Assert.Throws<InvalidDataException>(() =>
            FileKind.EnsureRegularFile(path, "NamedPipe.cs"));
        Assert.Contains("not a regular file", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BoundedReadRejectsOversizedRegularFiles()
    {
        using var fixture = new TemporaryDirectory();
        var path = Path.Combine(fixture.Path, "Large.cs");
        File.WriteAllText(path, "12345");

        InvalidDataException exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            FileKind.ReadAllTextAsync(path, maxBytes: 4, CancellationToken.None));

        Assert.Contains("exceeds the maximum of 4 bytes", exception.Message, StringComparison.Ordinal);
    }

    [DllImport("libc", SetLastError = true, EntryPoint = "mkfifo", CharSet = CharSet.Ansi, BestFitMapping = false)]
    private static extern int MkFifo(string path, int mode);

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"CodeMetricsToolkit-filekind-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
