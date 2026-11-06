using System.Text.Json;
using System.Globalization;
using CodeMetricsToolkit.Core.Analysis;
using CodeMetricsToolkit.Tests.SchemaValidation;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed partial class CliAnalyzeTests
{
    [Fact]
    public async Task AnalyzeCommandSeparatesDeclaredProjectReferencesFromTransitiveCompilationReferences()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        string[] names = ["A", "B", "C"];
        foreach (var name in names)
        {
            var directory = Directory.CreateDirectory(Path.Combine(input.Path, name)).FullName;
            File.WriteAllText(Path.Combine(directory, name + ".csproj"), $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <Import Project="References.props" />
                </Project>
                """);
            var reference = name == "A" ? "B" : "C";
            File.WriteAllText(Path.Combine(directory, "References.props"), name == "C" ? "<Project />" : $"""
                <Project>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
                    <ProjectReference Include="../{reference}/{reference}.csproj" />
                    <ProjectReference Include="../Missing/Missing.csproj" Condition="'$(TargetFramework)' == 'net8.0'" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(directory, name + ".cs"),
                name == "A" ? "public class A : C { }" : $"public class {name} {{ }}");
        }

        var solution = Path.Combine(input.Path, "Chain.sln");
        string[][] commands =
        [
            ["new", "sln", "--name", "Chain", "--format", "sln"],
            ["sln", "Chain.sln", "add", "A/A.csproj", "B/B.csproj", "C/C.csproj"],
            ["restore", "Chain.sln", "--disable-parallel", "-m:1", "-nr:false"]
        ];
        foreach (var command in commands)
        {
            DotnetRestoreRunner.ProcessResult result = await DotnetRestoreRunner.RunProcessAsync(
                DotnetMuxer.Resolve(), command, input.Path, CancellationToken.None);
            Assert.True(result.ExitCode == 0, result.StandardOutput + result.StandardError);
        }
        Assert.Equal(0, await RunCliAllowFailureAsync("analyze", solution, "--no-restore", "--output", output.Path));
        JsonElement[] nodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        string ProjectId(string name) => Assert.Single(nodes, node => HasPropertyValue(node, "kind", "project") &&
            HasPropertyValue(node, "name", name)).GetProperty("id").GetString()!;
        SchemaAssertions.OutputDirectoryValidates(output.Path);
        JsonElement[] edges = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "edges");
        JsonElement[] projectEdges = edges.Where(edge => HasPropertyValue(edge, "kind", "depends_on") &&
            edge.GetProperty("from").GetString()!.StartsWith("project:", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, projectEdges.Length);
        Assert.Contains(projectEdges, edge => HasPropertyValue(edge, "from", ProjectId("A")) && HasPropertyValue(edge, "to", ProjectId("B")));
        Assert.Contains(projectEdges, edge => HasPropertyValue(edge, "from", ProjectId("B")) && HasPropertyValue(edge, "to", ProjectId("C")));
        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        Assert.Equal(1, MetricInt(metrics, ProjectId("A"), "outgoing_project_dependency_count"));
        Assert.Equal(1, MetricInt(metrics, ProjectId("C"), "incoming_project_dependency_count"));
        Assert.Equal(1, MetricInt(metrics, "type:A/T:A", "inheritance_depth"));
        Assert.DoesNotContain(ReadNdjson(Path.Combine(output.Path, "diagnostics.ndjson")), row => HasPropertyValue(row, "severity", "error"));
    }

    [Fact]
    public async Task AnalyzeCommandPreservesLocalizedBuildWarnings()
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru-RU");
            await AnalyzeCommandPreservesBuildDiagnosticSeverity("Warning", 0, "trusted", "workspace_load_warning");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Theory]
    [InlineData("Warning", 0, "trusted", "workspace_load_warning")]
    [InlineData("Error", 3, "degraded", "workspace_load_failed")]
    [InlineData("WarningAsError", 3, "degraded", "workspace_load_failed")]
    [InlineData("WarningAndError", 3, "degraded", "workspace_load_failed")]
    public async Task AnalyzeCommandPreservesBuildDiagnosticSeverity(string task, int expectedExit, string quality, string diagnosticId)
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        var taskName = task == "Error" ? "Error" : "Warning";
        var additionalError = task == "WarningAndError" ? "<Error Code=\"TEST1234\" Text=\"Controlled build diagnostic\" />" : "";
        File.WriteAllText(Path.Combine(input.Path, "Sample.csproj"), $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <WarningsAsErrors>{(task == "WarningAsError" ? "TEST1234" : "")}</WarningsAsErrors>
              </PropertyGroup>
              <Target Name="DiagnosticFixture" BeforeTargets="CoreCompile" Condition="'$(DesignTimeBuild)' == 'true'">
                <{taskName} Code="TEST1234" Text="Controlled build diagnostic" />
                {additionalError}
              </Target>
            </Project>
            """);
        File.WriteAllText(Path.Combine(input.Path, "Sample.cs"), "public class Sample { public int Value() => 1; }");

        Assert.Equal(expectedExit, await RunCliAllowFailureAsync("analyze", input.Path, "--output", output.Path));
        SchemaAssertions.OutputDirectoryValidates(output.Path);
        using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(output.Path, "summary.json")));
        Assert.Equal(quality, summary.RootElement.GetProperty("analysisHealth").GetProperty("analysisQuality").GetString());
        Assert.Contains(ReadNdjson(Path.Combine(output.Path, "diagnostics.ndjson")), row =>
            HasPropertyValue(row, "id", diagnosticId) && row.GetProperty("message").GetString()!.Contains("Controlled build diagnostic", StringComparison.Ordinal));
    }
}
