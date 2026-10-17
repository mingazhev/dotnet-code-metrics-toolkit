using System.Globalization;
using CodeMetricsToolkit.Core.Contracts;
using CodeMetricsToolkit.Core.Discovery;
using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;

namespace CodeMetricsToolkit.Core.Syntax;

internal sealed class AnalysisDiagnosticCollector
{
    private readonly List<AnalysisDiagnostic> _diagnostics = [];
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);

    public IReadOnlyList<AnalysisDiagnostic> Diagnostics => _diagnostics;

    public static AnalysisDiagnostic FromSyntax(
        Diagnostic diagnostic,
        DiscoveredSourceFile sourceFile)
    {
        FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();

        return new AnalysisDiagnostic
        {
            Id = diagnostic.Id,
            Severity = ToSeverity(diagnostic.Severity),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            ProjectPath = sourceFile.ProjectPath,
            FilePath = sourceFile.RelativePath,
            StartLine = ToOneBasedLine(lineSpan.StartLinePosition.Line),
            EndLine = ToOneBasedLine(lineSpan.EndLinePosition.Line),
            Tags = ["syntax"]
        };
    }

    public static AnalysisDiagnostic FromCompilation(
        string rootPath,
        string projectPath,
        Diagnostic diagnostic)
    {
        FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
        var filePath = diagnostic.Location.SourceTree?.FilePath is { Length: > 0 } fullPath
            ? Path.GetRelativePath(rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/')
            : null;
        int? startLine = diagnostic.Location.IsInSource
            ? ToOneBasedLine(lineSpan.StartLinePosition.Line)
            : null;
        int? endLine = diagnostic.Location.IsInSource
            ? ToOneBasedLine(lineSpan.EndLinePosition.Line)
            : null;

        return new AnalysisDiagnostic
        {
            Id = diagnostic.Id,
            Severity = ToSeverity(diagnostic.Severity),
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            ProjectPath = projectPath,
            FilePath = filePath,
            StartLine = startLine,
            EndLine = endLine,
            Tags = CreateTags(diagnostic)
        };
    }

    public void Add(AnalysisDiagnostic diagnostic)
    {
        var key = DiagnosticKey(diagnostic);

        if (_positions.TryGetValue(key, out var position))
        {
            AnalysisDiagnostic existing = _diagnostics[position];
            _diagnostics[position] = existing with { Tags = MergeTags(existing.Tags, diagnostic.Tags) };
            return;
        }

        if (_diagnostics.Count >= ArtifactLimits.MaxRecords)
        {
            throw new InvalidDataException(
                $"Analysis produced more than {ArtifactLimits.MaxRecords} diagnostics.");
        }

        _positions.Add(key, _diagnostics.Count);
        _diagnostics.Add(diagnostic);
    }

    private static List<string> CreateTags(Diagnostic diagnostic)
    {
        List<string> tags = diagnostic.Id.StartsWith("CS", StringComparison.Ordinal)
            ? ["compiler"]
            : ["analyzer"];

        if (IsNullableDiagnostic(diagnostic.Id))
        {
            tags.Add("nullable");
        }

        return tags;
    }

    private static bool IsNullableDiagnostic(string diagnosticId)
    {
        return diagnosticId.StartsWith("CS86", StringComparison.Ordinal) ||
            diagnosticId.StartsWith("CS87", StringComparison.Ordinal) ||
            diagnosticId.StartsWith("CS88", StringComparison.Ordinal);
    }

    private static string DiagnosticKey(AnalysisDiagnostic diagnostic)
    {
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{diagnostic.Id}\n{diagnostic.ProjectPath}\n{diagnostic.FilePath}\n{diagnostic.StartLine}\n{diagnostic.EndLine}\n{diagnostic.Message}");
    }

    private static string[]? MergeTags(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        var tags = (left ?? [])
            .Concat(right ?? [])
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return tags.Length == 0 ? null : tags;
    }

    private static string ToSeverity(DiagnosticSeverity severity)
    {
        return severity switch
        {
            DiagnosticSeverity.Hidden or DiagnosticSeverity.Info => "info",
            DiagnosticSeverity.Warning => "warning",
            DiagnosticSeverity.Error => "error",
            _ => "warning"
        };
    }

    private static int ToOneBasedLine(int zeroBasedLine)
    {
        return zeroBasedLine + 1;
    }
}
