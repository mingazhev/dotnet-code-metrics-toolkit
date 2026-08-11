namespace CodeMetricsToolkit.Scoring;

internal static class GlobMatcher
{
    public static bool IsMatch(string pattern, string path)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(path);

        var normalizedPattern = Normalize(pattern);
        var normalizedPath = Normalize(path);
        var memo = new Dictionary<(int PatternIndex, int PathIndex), bool>();

        return Match(normalizedPattern, normalizedPath, 0, 0, memo);
    }

    private static bool Match(
        string pattern,
        string path,
        int patternIndex,
        int pathIndex,
        Dictionary<(int PatternIndex, int PathIndex), bool> memo)
    {
        if (memo.TryGetValue((patternIndex, pathIndex), out var cached))
        {
            return cached;
        }

        bool result;
        if (patternIndex == pattern.Length)
        {
            result = pathIndex == path.Length;
        }
        else if (IsDoubleStarDirectory(pattern, patternIndex))
        {
            result = Match(pattern, path, patternIndex + 3, pathIndex, memo) ||
                (pathIndex < path.Length &&
                 Match(pattern, path, patternIndex, pathIndex + 1, memo));
        }
        else if (IsDoubleStar(pattern, patternIndex))
        {
            result = Match(pattern, path, patternIndex + 2, pathIndex, memo) ||
                (pathIndex < path.Length &&
                 Match(pattern, path, patternIndex, pathIndex + 1, memo));
        }
        else if (pattern[patternIndex] == '*')
        {
            result = Match(pattern, path, patternIndex + 1, pathIndex, memo) ||
                (pathIndex < path.Length &&
                 path[pathIndex] != '/' &&
                 Match(pattern, path, patternIndex, pathIndex + 1, memo));
        }
        else if (pattern[patternIndex] == '?')
        {
            result = pathIndex < path.Length &&
                path[pathIndex] != '/' &&
                Match(pattern, path, patternIndex + 1, pathIndex + 1, memo);
        }
        else
        {
            result = pathIndex < path.Length &&
                pattern[patternIndex] == path[pathIndex] &&
                Match(pattern, path, patternIndex + 1, pathIndex + 1, memo);
        }

        memo[(patternIndex, pathIndex)] = result;

        return result;
    }

    private static bool IsDoubleStar(string pattern, int index)
    {
        return index + 1 < pattern.Length &&
            pattern[index] == '*' &&
            pattern[index + 1] == '*';
    }

    private static bool IsDoubleStarDirectory(string pattern, int index)
    {
        return IsDoubleStar(pattern, index) &&
            index + 2 < pattern.Length &&
            pattern[index + 2] == '/';
    }

    private static string Normalize(string value)
    {
        var normalized = value.Replace('\\', '/');

        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }
}
