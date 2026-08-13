using CodeMetricsToolkit.Core.Facts;

namespace CodeMetricsToolkit.Tests.Support;

internal static class TestFacts
{
    public static SyntaxAnalysisFacts Analysis(
        IReadOnlyList<FileFacts>? files = null,
        IReadOnlyList<TypeFacts>? types = null,
        IReadOnlyList<MemberFacts>? members = null,
        IReadOnlyList<GraphEdgeFacts>? edges = null,
        IReadOnlyList<AnalysisDiagnostic>? diagnostics = null,
        IReadOnlyList<string>? projects = null,
        bool trusted = true,
        bool trustedDiagnostics = true,
        bool diagnosticsInHotspots = true,
        string rootPath = ".")
    {
        return new SyntaxAnalysisFacts
        {
            Mode = trusted ? "semantic" : "partial_semantic",
            Health = new AnalysisHealth
            {
                AnalysisQuality = trusted ? "trusted" : "degraded",
                SemanticModel = trusted ? "complete" : "partial",
                RestoreStatus = "succeeded",
                BuildStatus = "not_run",
                TrustedDiagnostics = trustedDiagnostics,
                DiagnosticsIncludedInHotspotRank = diagnosticsInHotspots,
                Messages = []
            },
            RootPath = rootPath,
            ProjectPaths = projects ?? [],
            Files = files ?? [],
            Types = types ?? [],
            Members = members ?? [],
            GraphEdges = edges ?? [],
            Diagnostics = diagnostics ?? []
        };
    }

    public static FileFacts File(
        string id,
        string path,
        string projectKey = "project",
        int lines = 10,
        int tokenLines = 6,
        int blanks = 1,
        int commentOnly = 2,
        int commented = 3,
        int mixed = 1,
        int documentation = 1)
    {
        return new FileFacts
        {
            TargetId = id,
            TargetIdStability = "semantic",
            ProjectKey = projectKey,
            FilePath = path,
            StartLine = 1,
            EndLine = lines,
            LinesOfCode = lines,
            NonCommentLinesOfCode = tokenLines,
            BlankLineCount = blanks,
            CommentOnlyLineCount = commentOnly,
            CommentedLineCount = commented,
            MixedCodeCommentLineCount = mixed,
            DocumentationCommentLineCount = documentation
        };
    }

    public static TypeFacts Type(
        string id,
        string fileId,
        string path,
        string projectKey = "project",
        int startLine = 1,
        int endLine = 10,
        int memberCount = 1,
        TypeSemanticFacts? semantic = null)
    {
        return new TypeFacts
        {
            TargetId = id,
            TargetIdStability = "semantic",
            ParentFileTargetId = fileId,
            ProjectKey = projectKey,
            Name = id,
            FilePath = path,
            StartLine = startLine,
            EndLine = endLine,
            Declarations = [Span(path, startLine, endLine)],
            LinesOfCode = endLine - startLine + 1,
            NonCommentLinesOfCode = Math.Max(0, endLine - startLine),
            MemberCount = memberCount,
            Semantic = semantic
        };
    }

    public static MemberFacts Member(
        string id,
        string typeId,
        string path,
        string projectKey = "project",
        int startLine = 2,
        int endLine = 5,
        int parameterCount = 1,
        int cyclomatic = 2,
        int decisions = 1,
        int cognitive = 2,
        int nesting = 1,
        OperationFacts? operations = null)
    {
        return new MemberFacts
        {
            TargetId = id,
            TargetIdStability = "semantic",
            ParentTypeTargetId = typeId,
            ProjectKey = projectKey,
            Name = id,
            ChunkKind = "member_body",
            FilePath = path,
            StartLine = startLine,
            EndLine = endLine,
            Declarations = [Span(path, startLine, endLine)],
            ControlFlow = new ControlFlowFacts
            {
                CyclomaticComplexity = cyclomatic,
                DecisionPointCount = decisions,
                CognitiveComplexity = cognitive,
                NestingDepth = nesting
            },
            MethodLength = endLine - startLine + 1,
            ParameterCount = parameterCount,
            Operations = operations
        };
    }

    public static GraphEdgeFacts Edge(string from, string to, string kind)
    {
        return new GraphEdgeFacts
        {
            From = from,
            To = to,
            Kind = kind,
            Confidence = "exact"
        };
    }

    public static SourceSpanFacts Span(string path, int startLine, int endLine)
    {
        return new SourceSpanFacts
        {
            FilePath = path,
            StartLine = startLine,
            EndLine = endLine
        };
    }
}
