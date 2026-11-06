using Microsoft.Build.Evaluation;
using Microsoft.Build.Logging;
using Microsoft.CodeAnalysis;

namespace CodeMetricsToolkit.Core.Analysis;

internal sealed class WorkspaceBuildLog : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("codemetrics-msbuild-").FullName;
    private readonly Dictionary<string, HashSet<string>> references = new(StringComparer.Ordinal);
    private readonly List<(string Project, string Text)> warnings = [];
    private readonly List<(string Project, string Text)> errors = [];

    public BinaryLogger CreateLogger() => new() { Parameters = Path.Combine(directory, $"{Guid.NewGuid():N}.binlog") };

    public void Read(CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(directory, "*.binlog"))
        {
            var replay = new BinaryLogReplayEventSource();
            replay.ProjectStarted += (_, args) =>
            {
                if (args.ProjectFile is null)
                {
                    return;
                }

                var projectPath = Path.GetFullPath(args.ProjectFile);
                if (!references.TryGetValue(projectPath, out HashSet<string>? paths))
                {
                    paths = new HashSet<string>(StringComparer.Ordinal);
                    references.Add(projectPath, paths);
                }

                cancellationToken.ThrowIfCancellationRequested();
                // Evaluate before targets add implicit transitive references, using this build's configuration.
                using var projects = new ProjectCollection(args.GlobalProperties);
                Microsoft.Build.Evaluation.Project project = projects.LoadProject(projectPath);
                foreach (ProjectItem item in project.GetItems("ProjectReference"))
                {
                    paths.Add(Path.GetFullPath(item.EvaluatedInclude, Path.GetDirectoryName(projectPath)!));
                }
            };
            replay.WarningRaised += (_, args) => warnings.Add((args.ProjectFile ?? "",
                args.Message ?? ""));
            replay.ErrorRaised += (_, args) => errors.Add((args.ProjectFile ?? "",
                args.Message ?? ""));
            replay.Replay(path, cancellationToken);
        }
    }

    public IReadOnlySet<string> GetProjectReferences(string projectPath)
    {
        return references.TryGetValue(Path.GetFullPath(projectPath), out HashSet<string>? paths)
            ? paths
            : throw new InvalidOperationException($"MSBuild did not record evaluated references for '{projectPath}'.");
    }

    public WorkspaceDiagnostic Classify(WorkspaceDiagnostic diagnostic)
    {
        // Roslyn 5.6 DiagnosticReporter flattens build warnings into workspace failures.
        bool Matches((string Project, string Text) item) => item.Project.Length > 0 &&
            diagnostic.Message.Contains(item.Project, StringComparison.Ordinal) &&
            item.Text.Length > 0 && diagnostic.Message.EndsWith($": {item.Text}", StringComparison.Ordinal);
        return diagnostic.Kind == WorkspaceDiagnosticKind.Failure && warnings.Any(Matches) && !errors.Any(Matches)
            ? new WorkspaceDiagnostic(WorkspaceDiagnosticKind.Warning, diagnostic.Message)
            : diagnostic;
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
