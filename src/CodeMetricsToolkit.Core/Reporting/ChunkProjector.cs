using System.Security.Cryptography;
using System.Text;
using CodeMetricsToolkit.Abstractions;
using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis.Text;

namespace CodeMetricsToolkit.Core.Reporting;

public static class ChunkProjector
{
    public static IReadOnlyList<ChunkLine> Project(
        SyntaxAnalysisFacts facts,
        bool includeText,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(facts);

        var chunks = new List<ChunkLine>();
        IReadOnlyDictionary<string, string> sourceSnapshots = facts.SourceTextSnapshots;
        var typesByFile = facts.Types
            .SelectMany(type => type.Declarations.Select(declaration => (declaration.FilePath, Type: type)))
            .GroupBy(entry => entry.FilePath, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(entry => entry.Type).Distinct().ToArray(),
                StringComparer.Ordinal);
        var membersByType = facts.Members
            .GroupBy(member => member.ParentTypeTargetId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var edgesByFrom = facts.GraphEdges
            .GroupBy(edge => edge.From, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (FileFacts file in facts.Files.OrderBy(file => file.FilePath, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceSpanFacts span = CreateFileHeaderSpan(file, typesByFile);
            IReadOnlyList<string> relatedTargetIds = typesByFile.TryGetValue(file.FilePath, out TypeFacts[]? types)
                ? types.Select(type => type.TargetId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()
                : [];

            chunks.Add(CreateChunk(
                sourceSnapshots,
                file.TargetId,
                "file",
                file.TargetIdStability,
                "file_header",
                span,
                relatedTargetIds,
                includeText));
        }

        foreach (TypeFacts type in facts.Types.OrderBy(type => type.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> relatedTargetIds = (membersByType.TryGetValue(type.TargetId, out MemberFacts[]? members)
                    ? members.Select(member => member.TargetId)
                    : [])
                .Append(type.ParentFileTargetId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            foreach (SourceSpanFacts declaration in type.Declarations)
            {
                chunks.Add(CreateChunk(
                    sourceSnapshots,
                    type.TargetId,
                    "type",
                    type.TargetIdStability,
                    "type_declaration",
                    declaration,
                    relatedTargetIds,
                    includeText));
            }
        }

        foreach (MemberFacts member in facts.Members.OrderBy(member => member.TargetId, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            IReadOnlyList<string> relatedTargetIds = (edgesByFrom.TryGetValue(member.TargetId, out GraphEdgeFacts[]? edges)
                    ? edges.Select(edge => edge.To)
                    : [])
                .Append(member.ParentTypeTargetId)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();

            foreach (SourceSpanFacts declaration in member.Declarations)
            {
                chunks.Add(CreateChunk(
                    sourceSnapshots,
                    member.TargetId,
                    "member",
                    member.TargetIdStability,
                    member.ChunkKind,
                    declaration,
                    relatedTargetIds,
                    includeText));
            }
        }

        return chunks;
    }

    private static SourceSpanFacts CreateFileHeaderSpan(
        FileFacts file,
        Dictionary<string, TypeFacts[]> typesByFile)
    {
        var firstTypeLine = (typesByFile.TryGetValue(file.FilePath, out TypeFacts[]? types)
                ? types.SelectMany(type => type.Declarations)
                : [])
            .Where(declaration => declaration.FilePath == file.FilePath)
            .Select(declaration => declaration.StartLine)
            .DefaultIfEmpty(file.EndLine + 1)
            .Min();

        var endLine = firstTypeLine > file.StartLine
            ? firstTypeLine - 1
            : file.StartLine;

        return new SourceSpanFacts
        {
            FilePath = file.FilePath,
            StartLine = file.StartLine,
            EndLine = Math.Min(file.EndLine, endLine)
        };
    }

    private static ChunkLine CreateChunk(
        IReadOnlyDictionary<string, string> sourceSnapshots,
        string targetId,
        string targetKind,
        string targetIdStability,
        string chunkKind,
        SourceSpanFacts span,
        IReadOnlyList<string> relatedTargetIds,
        bool includeText)
    {
        var text = ReadLineRange(sourceSnapshots, span);

        return new ChunkLine
        {
            SchemaVersion = ContractVersion.Current,
            ChunkId = $"chunk:{targetId}#{chunkKind}@{span.FilePath}:{span.StartLine}",
            TargetId = targetId,
            TargetKind = targetKind,
            TargetIdStability = targetIdStability,
            ChunkKind = chunkKind,
            FilePath = span.FilePath,
            StartLine = span.StartLine,
            EndLine = span.EndLine,
            TokenEstimate = EstimateTokens(text),
            TextHash = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(),
            RelatedTargetIds = relatedTargetIds,
            Text = includeText ? text : null
        };
    }

    private static string ReadLineRange(
        IReadOnlyDictionary<string, string> sourceSnapshots,
        SourceSpanFacts span)
    {
        if (!sourceSnapshots.TryGetValue(span.FilePath, out var snapshot))
        {
            throw new InvalidOperationException(
                $"No collected source snapshot exists for '{span.FilePath}'.");
        }

        var sourceText = SourceText.From(snapshot);
        if (sourceText.Length == 0)
        {
            return string.Empty;
        }

        var startIndex = Math.Clamp(span.StartLine - 1, 0, sourceText.Lines.Count - 1);
        var endIndex = Math.Clamp(span.EndLine - 1, startIndex, sourceText.Lines.Count - 1);

        return string.Join(
            '\n',
            Enumerable.Range(startIndex, endIndex - startIndex + 1)
                .Select(index => sourceText.Lines[index].ToString()));
    }

    private static int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        return Math.Max(1, (int)Math.Ceiling(text.Length / 4.0));
    }
}
