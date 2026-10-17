using CodeMetricsToolkit.Core.Facts;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CodeMetricsToolkit.Core.Syntax;

internal static class DuplicationFactsCollector
{
    private const int MinimumWindowTokens = 100;
    private const int MinimumWindowLines = 10;
    private const int RankLevelCount = 7;
    private const string BlockTagPrefix = "duplication_block:";

    public static DuplicationFacts Collect(
        IReadOnlyList<SourceFileContext> sourceFiles,
        CancellationToken cancellationToken,
        DuplicationCollectorLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        cancellationToken.ThrowIfCancellationRequested();

        DuplicationCollectorLimits effectiveLimits = limits ?? DuplicationCollectorLimits.Default;
        ValidateLimits(effectiveLimits);
        var work = new WorkBudget(effectiveLimits.MaxWorkUnits, cancellationToken);
        FileTokenData[] files = CreateFileTokenData(
            sourceFiles,
            effectiveLimits,
            work,
            cancellationToken);
        AssignTokenRanks(files, work, cancellationToken);

        Dictionary<WindowKey, WindowGroup> repeatedWindows = CollectWindows(
            files,
            effectiveLimits,
            work,
            cancellationToken);
        var duplicatedStarts = files
            .Select(file => new bool[Math.Max(0, file.Tokens.Length - MinimumWindowTokens + 1)])
            .ToArray();

        foreach (WindowGroup group in repeatedWindows.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Occurrences is null)
            {
                continue;
            }

            List<WindowOccurrence> occurrences = group.Occurrences;
            var firstFileIndex = occurrences[0].FileIndex;
            var occursInMultipleFiles = false;
            foreach (WindowOccurrence occurrence in occurrences)
            {
                work.Spend();
                if (occurrence.FileIndex != firstFileIndex)
                {
                    occursInMultipleFiles = true;
                    break;
                }
            }

            if (occursInMultipleFiles)
            {
                foreach (WindowOccurrence occurrence in occurrences)
                {
                    work.Spend();
                    AddDuplicatedWindow(duplicatedStarts, occurrence);
                }

                continue;
            }

            AddNonOverlappingOccurrences(occurrences, duplicatedStarts, work);
        }

        List<DuplicationRegionFacts>[] regionsByFile = files
            .Select(_ => new List<DuplicationRegionFacts>())
            .ToArray();
        var duplicatedLineCounts = new int[files.Length];

        for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileTokenData file = files[fileIndex];
            var mergedRegions = new List<TokenInterval>();
            for (var start = 0; start < duplicatedStarts[fileIndex].Length; start++)
            {
                work.Spend();
                if (!duplicatedStarts[fileIndex][start])
                {
                    continue;
                }

                TokenFacts first = file.Tokens[start];
                TokenFacts last = file.Tokens[start + MinimumWindowTokens - 1];
                var window = new TokenInterval(
                    start,
                    start + MinimumWindowTokens,
                    first.StartLine,
                    last.EndLine);
                if (mergedRegions.Count == 0 || window.StartToken >= mergedRegions[^1].EndToken)
                {
                    mergedRegions.Add(window);
                    continue;
                }

                TokenInterval current = mergedRegions[^1];
                mergedRegions[^1] = current with
                {
                    EndToken = Math.Max(current.EndToken, window.EndToken),
                    StartLine = Math.Min(current.StartLine, window.StartLine),
                    EndLine = Math.Max(current.EndLine, window.EndLine)
                };
            }

            duplicatedLineCounts[fileIndex] = CountUniqueLines(mergedRegions, work);
            regionsByFile[fileIndex].AddRange(mergedRegions.Select(region =>
                new DuplicationRegionFacts(region.StartLine, region.EndLine, string.Empty)));
        }

        return CreateResult(files, regionsByFile, duplicatedLineCounts, cancellationToken);
    }

    private static void ValidateLimits(DuplicationCollectorLimits limits)
    {
        if (limits.MaxTokens < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "The token limit cannot be negative.");
        }

        if (limits.MaxWindows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "The window limit cannot be negative.");
        }

        if (limits.MaxWorkUnits < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "The work limit cannot be negative.");
        }
    }

    private static FileTokenData[] CreateFileTokenData(
        IReadOnlyList<SourceFileContext> sourceFiles,
        DuplicationCollectorLimits limits,
        WorkBudget work,
        CancellationToken cancellationToken)
    {
        SourceFileContext[] uniqueSources = sourceFiles
            .GroupBy(context => context.SourceFile.RelativePath, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(context => context.SourceFile.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var files = new List<FileTokenData>(uniqueSources.Length);
        var totalTokenCount = 0;

        foreach (SourceFileContext source in uniqueSources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var tokens = new List<TokenFacts>();
            foreach (SyntaxToken token in source.Root.DescendantTokens(descendIntoTrivia: false))
            {
                work.Spend();
                if (token.IsMissing || token.IsKind(SyntaxKind.EndOfFileToken) || token.Span.IsEmpty)
                {
                    continue;
                }

                if (totalTokenCount >= limits.MaxTokens)
                {
                    throw new InvalidDataException(
                        $"Duplication analysis exceeds the configured token limit of {limits.MaxTokens} tokens.");
                }

                totalTokenCount++;
                var startLine = source.SourceText.Lines.GetLineFromPosition(token.Span.Start).LineNumber + 1;
                var endLine = source.SourceText.Lines.GetLineFromPosition(token.Span.End - 1).LineNumber + 1;
                tokens.Add(new TokenFacts(token.RawKind, token.Text, startLine, endLine));
            }

            files.Add(new FileTokenData(source.SourceFile.RelativePath, tokens.ToArray()));
        }

        return files.ToArray();
    }

    private static void AssignTokenRanks(
        IReadOnlyList<FileTokenData> files,
        WorkBudget work,
        CancellationToken cancellationToken)
    {
        var tokenRanks = new Dictionary<TokenKey, int>();
        var nextTokenRank = 0;

        foreach (FileTokenData file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ranks = new int[file.Tokens.Length];
            for (var tokenIndex = 0; tokenIndex < file.Tokens.Length; tokenIndex++)
            {
                work.Spend();
                TokenFacts token = file.Tokens[tokenIndex];
                var key = new TokenKey(token.RawKind, token.Text);
                if (!tokenRanks.TryGetValue(key, out var rank))
                {
                    rank = nextTokenRank++;
                    tokenRanks.Add(key, rank);
                }

                ranks[tokenIndex] = rank;
            }

            file.RankLevels[0] = ranks;
        }

        for (var level = 1; level < RankLevelCount; level++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var halfLength = 1 << (level - 1);
            var pairRanks = new Dictionary<RankPair, int>();
            var nextRank = 0;

            foreach (FileTokenData file in files)
            {
                var previousRanks = file.RankLevels[level - 1]!;
                var rankCount = Math.Max(0, previousRanks.Length - halfLength);
                var ranks = new int[rankCount];
                for (var start = 0; start < rankCount; start++)
                {
                    work.Spend();
                    var pair = new RankPair(previousRanks[start], previousRanks[start + halfLength]);
                    if (!pairRanks.TryGetValue(pair, out var rank))
                    {
                        rank = nextRank++;
                        pairRanks.Add(pair, rank);
                    }

                    ranks[start] = rank;
                }

                file.RankLevels[level] = ranks;
            }
        }
    }

    private static Dictionary<WindowKey, WindowGroup> CollectWindows(
        IReadOnlyList<FileTokenData> files,
        DuplicationCollectorLimits limits,
        WorkBudget work,
        CancellationToken cancellationToken)
    {
        var windows = new Dictionary<WindowKey, WindowGroup>();
        var windowCount = 0;

        for (var fileIndex = 0; fileIndex < files.Count; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileTokenData file = files[fileIndex];
            var windowStartCount = file.Tokens.Length - MinimumWindowTokens + 1;
            if (windowStartCount <= 0)
            {
                continue;
            }

            var rank64 = file.RankLevels[6]!;
            var rank32 = file.RankLevels[5]!;
            var rank4 = file.RankLevels[2]!;

            for (var start = 0; start < windowStartCount; start++)
            {
                work.Spend();
                if (++windowCount > limits.MaxWindows)
                {
                    throw new InvalidDataException(
                        $"Duplication analysis exceeds the configured window limit of {limits.MaxWindows} windows.");
                }

                TokenFacts first = file.Tokens[start];
                TokenFacts last = file.Tokens[start + MinimumWindowTokens - 1];
                if (last.EndLine - first.StartLine + 1 < MinimumWindowLines)
                {
                    continue;
                }

                var key = new WindowKey(rank64[start], rank32[start + 64], rank4[start + 96]);
                var occurrence = new WindowOccurrence(fileIndex, start, first.StartLine, last.EndLine);
                if (windows.TryGetValue(key, out WindowGroup group))
                {
                    group.Add(occurrence);
                    windows[key] = group;
                }
                else
                {
                    windows.Add(key, new WindowGroup(occurrence));
                }
            }
        }

        return windows;
    }

    private static void AddNonOverlappingOccurrences(
        IReadOnlyList<WindowOccurrence> occurrences,
        IReadOnlyList<bool[]> duplicatedStarts,
        WorkBudget work)
    {
        var firstStart = occurrences[0].TokenStart;
        var lastStart = occurrences[^1].TokenStart;
        for (var index = 0; index < occurrences.Count; index++)
        {
            work.Spend();
            WindowOccurrence current = occurrences[index];
            if (current.TokenStart - firstStart >= MinimumWindowTokens ||
                lastStart - current.TokenStart >= MinimumWindowTokens)
            {
                AddDuplicatedWindow(duplicatedStarts, current);
            }
        }
    }

    private static void AddDuplicatedWindow(
        IReadOnlyList<bool[]> duplicatedStarts,
        WindowOccurrence occurrence)
    {
        duplicatedStarts[occurrence.FileIndex][occurrence.TokenStart] = true;
    }

    private static int CountUniqueLines(
        IReadOnlyList<TokenInterval> regions,
        WorkBudget work)
    {
        if (regions.Count == 0)
        {
            return 0;
        }

        var count = 0;
        var currentStart = regions[0].StartLine;
        var currentEnd = regions[0].EndLine;
        for (var index = 1; index < regions.Count; index++)
        {
            work.Spend();
            var nextStart = regions[index].StartLine;
            var nextEnd = regions[index].EndLine;
            if (nextStart <= currentEnd + 1)
            {
                currentEnd = Math.Max(currentEnd, nextEnd);
                continue;
            }

            count = checked(count + currentEnd - currentStart + 1);
            currentStart = nextStart;
            currentEnd = nextEnd;
        }

        return checked(count + currentEnd - currentStart + 1);
    }

    private static DuplicationFacts CreateResult(
        FileTokenData[] files,
        List<DuplicationRegionFacts>[] regionsByFile,
        int[] duplicatedLineCounts,
        CancellationToken cancellationToken)
    {
        var results = new List<DuplicationFileFacts>(files.Length);
        var blockOrdinal = 0;

        for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var regions = new List<DuplicationRegionFacts>(regionsByFile[fileIndex].Count);
            foreach (DuplicationRegionFacts region in regionsByFile[fileIndex])
            {
                blockOrdinal++;
                regions.Add(region with
                {
                    Tag = BlockTagPrefix + blockOrdinal.ToString("D6", System.Globalization.CultureInfo.InvariantCulture)
                });
            }

            results.Add(new DuplicationFileFacts(
                files[fileIndex].FilePath,
                regions,
                duplicatedLineCounts[fileIndex]));
        }

        return new DuplicationFacts(results);
    }

    private sealed class FileTokenData(string filePath, TokenFacts[] tokens)
    {
        public string FilePath { get; } = filePath;
        public TokenFacts[] Tokens { get; } = tokens;
        public int[]?[] RankLevels { get; } = new int[]?[RankLevelCount];
    }

    private readonly record struct TokenFacts(int RawKind, string Text, int StartLine, int EndLine);

    private readonly record struct TokenKey(int RawKind, string Text);

    private readonly record struct RankPair(int First, int Second);

    private readonly record struct WindowKey(int First64, int Next32, int Final4);

    private readonly record struct WindowOccurrence(int FileIndex, int TokenStart, int StartLine, int EndLine);

    private readonly record struct TokenInterval(int StartToken, int EndToken, int StartLine, int EndLine);

    private struct WindowGroup(WindowOccurrence first)
    {
        public List<WindowOccurrence>? Occurrences { get; private set; }

        public void Add(WindowOccurrence occurrence)
        {
            if (Occurrences is null)
            {
                Occurrences = [first, occurrence];
                return;
            }

            Occurrences.Add(occurrence);
        }
    }

    private sealed class WorkBudget(long maximum, CancellationToken cancellationToken)
    {
        private const int CancellationCheckMask = 1023;
        private long Count { get; set; }

        public void Spend(long units = 1)
        {
            if (units > maximum - Count)
            {
                throw new InvalidDataException(
                    $"Duplication analysis exceeds the configured work limit of {maximum} units.");
            }

            Count += units;
            if ((Count & CancellationCheckMask) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
    }
}
