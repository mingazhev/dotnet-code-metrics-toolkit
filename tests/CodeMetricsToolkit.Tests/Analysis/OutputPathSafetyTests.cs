using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core;
using CodeMetricsToolkit.Core.Analysis;

namespace CodeMetricsToolkit.Tests.Analysis;

public sealed class OutputPathSafetyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OutputEqualToOriginalInputRootIsRejectedBeforeSourceCanBeReplaced(
        bool isolateInput)
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.CreateDirectory("input");
        var sourcePath = Path.Combine(inputPath, "Protected.cs");
        File.WriteAllText(sourcePath, "internal sealed class Protected { }");
        WriteForgedManifest(inputPath);

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            AnalyzeAsync(inputPath, inputPath, isolateInput));

        Assert.Contains("cannot be the analyzed input root", exception.Message, StringComparison.Ordinal);
        Assert.Equal("internal sealed class Protected { }", File.ReadAllText(sourcePath));
        Assert.True(File.Exists(Path.Combine(inputPath, ArtifactNames.Manifest)));
    }

    [Fact]
    public async Task OutputAncestorOfInputRootIsRejectedBeforeSourceCanBeReplaced()
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.CreateDirectory("repository");
        var sourcePath = Path.Combine(inputPath, "Protected.cs");
        var hostDataPath = Path.Combine(workspace.RootPath, "host-data.txt");
        File.WriteAllText(sourcePath, "internal sealed class Protected { }");
        File.WriteAllText(hostDataPath, "keep");
        WriteForgedManifest(workspace.RootPath);

        ArgumentException exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            AnalyzeAsync(inputPath, workspace.RootPath, isolateInput: false));

        Assert.Contains("or one of its ancestors", exception.Message, StringComparison.Ordinal);
        Assert.Equal("internal sealed class Protected { }", File.ReadAllText(sourcePath));
        Assert.Equal("keep", File.ReadAllText(hostDataPath));
    }

    [Fact]
    public async Task OutputDescendantOfInputRootRemainsAllowed()
    {
        using var workspace = new TemporaryWorkspace();
        var inputPath = workspace.CreateDirectory("input");
        var sourcePath = Path.Combine(inputPath, "Allowed.cs");
        var outputPath = Path.Combine(inputPath, "generated-output");
        File.WriteAllText(sourcePath, "internal sealed class Allowed { }");

        AnalysisRunResult result = await AnalyzeAsync(
            inputPath,
            outputPath,
            isolateInput: false);

        Assert.True(File.Exists(Path.Combine(result.OutputPath, ArtifactNames.Manifest)));
        Assert.True(File.Exists(Path.Combine(outputPath, ArtifactNames.Manifest)));
        Assert.Equal("internal sealed class Allowed { }", File.ReadAllText(sourcePath));
    }

    [Fact]
    public async Task MacOsRootAliasCannotBypassInputOutputOverlapCheck()
    {
        if (!OperatingSystem.IsMacOS() || !Directory.Exists("/tmp"))
        {
            return;
        }

        var physicalInput = Path.Combine(
            "/private/tmp",
            $"CodeMetricsToolkit-alias-overlap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(physicalInput);

        try
        {
            var sourcePath = Path.Combine(physicalInput, "Protected.cs");
            File.WriteAllText(sourcePath, "internal sealed class Protected { }");
            WriteForgedManifest(physicalInput);
            var aliasedOutput = "/tmp" + physicalInput["/private/tmp".Length..];

            await Assert.ThrowsAsync<ArgumentException>(() =>
                AnalyzeAsync(physicalInput, aliasedOutput, isolateInput: false));

            Assert.Equal("internal sealed class Protected { }", File.ReadAllText(sourcePath));
        }
        finally
        {
            Directory.Delete(physicalInput, recursive: true);
        }
    }

    [Fact]
    public async Task PhysicalMacOsOutputCannotBypassAliasedInputOverlapCheck()
    {
        if (!OperatingSystem.IsMacOS() || !Directory.Exists("/tmp"))
        {
            return;
        }

        var physicalInput = Path.Combine(
            "/private/tmp",
            $"CodeMetricsToolkit-reverse-alias-overlap-{Guid.NewGuid():N}");
        Directory.CreateDirectory(physicalInput);

        try
        {
            var sourcePath = Path.Combine(physicalInput, "Protected.cs");
            File.WriteAllText(sourcePath, "internal sealed class Protected { }");
            WriteForgedManifest(physicalInput);
            var aliasedInput = "/tmp" + physicalInput["/private/tmp".Length..];

            await Assert.ThrowsAsync<ArgumentException>(() =>
                AnalyzeAsync(aliasedInput, physicalInput, isolateInput: false));

            Assert.Equal("internal sealed class Protected { }", File.ReadAllText(sourcePath));
        }
        finally
        {
            Directory.Delete(physicalInput, recursive: true);
        }
    }

    [Fact]
    public async Task NestedDirectorySymlinkCannotBypassInputOutputOverlapCheck()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var workspace = new TemporaryWorkspace();
        var realInput = workspace.CreateDirectory("real-input");
        var sourcePath = Path.Combine(realInput, "Protected.cs");
        File.WriteAllText(sourcePath, "internal sealed class Protected { }");
        WriteForgedManifest(realInput);
        var linkInput = Path.Combine(workspace.RootPath, "link-input");
        Directory.CreateSymbolicLink(linkInput, realInput);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            AnalyzeAsync(linkInput, realInput, isolateInput: false));

        Assert.Equal("internal sealed class Protected { }", File.ReadAllText(sourcePath));
    }

    private static Task<AnalysisRunResult> AnalyzeAsync(
        string inputPath,
        string outputPath,
        bool isolateInput)
    {
        return CodeMetricsAnalyzer.AnalyzeAsync(
            new AnalyzeRequest
            {
                InputPath = inputPath,
                OutputPath = outputPath,
                SyntaxOnly = true,
                NoRestore = true,
                IsolateInput = isolateInput
            },
            CancellationToken.None);
    }

    private static void WriteForgedManifest(string directoryPath)
    {
        File.WriteAllText(
            Path.Combine(directoryPath, ArtifactNames.Manifest),
            $$"""{"tool":"{{ToolkitInfo.Name}}"}""");
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        public TemporaryWorkspace()
        {
            RootPath = Path.Combine(
                Path.GetTempPath(),
                $"CodeMetricsToolkit-output-safety-{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public string CreateDirectory(string name)
        {
            var path = Path.Combine(RootPath, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}
