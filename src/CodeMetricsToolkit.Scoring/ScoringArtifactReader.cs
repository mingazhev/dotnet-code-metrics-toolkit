using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodeMetricsToolkit.Scoring;

internal static class ScoringArtifactReader
{
    private static readonly HashSet<string> TargetKinds =
        new(["solution", "project", "file", "type", "member"], StringComparer.Ordinal);
    private static readonly HashSet<string> TargetIdStabilities =
        new(["semantic", "syntax_fallback", "line_fallback"], StringComparer.Ordinal);

    public static async Task<ScoringArtifacts> ReadAsync(
        string artifactDirectory,
        ScoringInputLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        EnsureRegularDirectory(artifactDirectory);
        var manifestPath = Path.Combine(artifactDirectory, ArtifactNames.Manifest);
        var summaryPath = Path.Combine(artifactDirectory, ArtifactNames.Summary);
        var metricsPath = Path.Combine(artifactDirectory, ArtifactNames.Metrics);
        var graphPath = Path.Combine(artifactDirectory, ArtifactNames.Graph);

        EnsureFileExists(manifestPath, ArtifactNames.Manifest);
        EnsureFileExists(summaryPath, ArtifactNames.Summary);
        EnsureFileExists(metricsPath, ArtifactNames.Metrics);
        EnsureFileExists(graphPath, ArtifactNames.Graph);

        var manifestBefore = await ReadSnapshotAsync(
            manifestPath,
            ArtifactNames.Manifest,
            limits.MaxJsonArtifactBytes,
            cancellationToken).ConfigureAwait(false);
        ArtifactManifest manifest = ParseManifest(manifestBefore, limits.MaxJsonDepth);
        var manifestSha256 = Hash(manifestBefore);
        var summaryBytes = await ReadSnapshotAsync(
            summaryPath,
            ArtifactNames.Summary,
            limits.MaxJsonArtifactBytes,
            cancellationToken).ConfigureAwait(false);

        ArtifactSummary summary = ParseSummary(summaryBytes, limits.MaxJsonDepth);
        var summarySha256 = Hash(summaryBytes);
        MetricsSnapshot metricsSnapshot = await ReadMetricsSnapshotAsync(
                metricsPath,
                limits,
                cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<ArtifactMetric> metrics = metricsSnapshot.Metrics;
        var graphBytes = await ReadSnapshotAsync(
            graphPath,
            ArtifactNames.Graph,
            limits.MaxJsonArtifactBytes,
            cancellationToken).ConfigureAwait(false);

        ArtifactGraph graph = ParseGraph(graphBytes, limits.MaxJsonDepth);
        var graphSha256 = Hash(graphBytes);
        var manifestAfter = await ReadSnapshotAsync(
            manifestPath,
            ArtifactNames.Manifest,
            limits.MaxJsonArtifactBytes,
            cancellationToken).ConfigureAwait(false);
        if (!manifestBefore.AsSpan().SequenceEqual(manifestAfter))
        {
            throw InvalidArtifacts(
                "manifest.json changed while scoring artifacts were read. Retry after the producer " +
                "publishes one complete artifact generation.");
        }

        if (metrics.Count != summary.MetricResultCount)
        {
            throw InvalidArtifacts(
                $"summary.json declares metricResultCount={summary.MetricResultCount}, " +
                $"but metrics.ndjson contains {metrics.Count} non-empty lines.");
        }

        foreach (ArtifactMetric metric in metrics)
        {
            if (!string.Equals(metric.SchemaVersion, summary.SchemaVersion, StringComparison.Ordinal))
            {
                throw InvalidArtifacts(
                    $"Metric '{metric.MetricId}@{metric.MetricVersion}' for target '{metric.TargetId}' " +
                    $"uses schemaVersion '{metric.SchemaVersion}', while summary.json uses " +
                    $"'{summary.SchemaVersion}'.");
            }
        }

        if (!string.Equals(graph.SchemaVersion, summary.SchemaVersion, StringComparison.Ordinal))
        {
            throw InvalidArtifacts(
                $"graph.json uses schemaVersion '{graph.SchemaVersion}', while summary.json uses " +
                $"'{summary.SchemaVersion}'.");
        }

        if (!string.Equals(manifest.SchemaVersion, summary.SchemaVersion, StringComparison.Ordinal))
        {
            throw InvalidArtifacts(
                $"manifest.json uses schemaVersion '{manifest.SchemaVersion}', while summary.json uses " +
                $"'{summary.SchemaVersion}'.");
        }

        EnsureUniqueMetrics(metrics);
        ValidateMetricTargets(metrics, graph);

        return new ScoringArtifacts(
            manifest.Mode,
            summary,
            metrics,
            graph,
            manifestSha256,
            summarySha256,
            metricsSnapshot.Sha256,
            graphSha256);
    }

    private static ArtifactManifest ParseManifest(byte[] json, int maxJsonDepth)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = maxJsonDepth });
            JsonElement root = RequireObject(document.RootElement, "manifest.json");

            var schemaVersion = GetRequiredNonBlankString(root, "schemaVersion", "manifest.json");
            var mode = GetRequiredNonBlankString(root, "mode", "manifest.json");

            if (mode is not ("syntax" or "semantic" or "partial_semantic"))
            {
                throw InvalidArtifacts($"manifest.json.mode '{mode}' is not supported.");
            }

            return new ArtifactManifest(schemaVersion, mode);
        }
        catch (ScoringException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw InvalidArtifacts("manifest.json is not valid JSON.", exception);
        }
    }

    private static ArtifactSummary ParseSummary(byte[] json, int maxJsonDepth)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = maxJsonDepth });
            JsonElement root = RequireObject(document.RootElement, "summary.json");
            JsonElement health = RequireObject(
                GetRequiredProperty(root, "analysisHealth", "summary.json"),
                "summary.json.analysisHealth");

            var schemaVersion = GetRequiredNonBlankString(root, "schemaVersion", "summary.json");
            var rootPath = GetRequiredNonBlankString(root, "rootPath", "summary.json");
            var metricResultCount = GetRequiredNonNegativeInteger(
                root,
                "metricResultCount",
                "summary.json");
            var analysisQuality = GetRequiredNonBlankString(
                health,
                "analysisQuality",
                "summary.json.analysisHealth");
            var trustedDiagnostics = GetRequiredBoolean(
                health,
                "trustedDiagnostics",
                "summary.json.analysisHealth");

            return new ArtifactSummary(
                schemaVersion,
                rootPath,
                metricResultCount,
                analysisQuality,
                trustedDiagnostics);
        }
        catch (ScoringException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw InvalidArtifacts("summary.json is not valid JSON.", exception);
        }
    }

    private static ArtifactGraph ParseGraph(byte[] json, int maxJsonDepth)
    {
        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = maxJsonDepth });
            JsonElement root = RequireObject(document.RootElement, "graph.json");
            var schemaVersion = GetRequiredNonBlankString(root, "schemaVersion", "graph.json");
            JsonElement nodes = GetRequiredProperty(root, "nodes", "graph.json");
            if (nodes.ValueKind != JsonValueKind.Array)
            {
                throw InvalidArtifacts("graph.json.nodes must be an array.");
            }

            var targets = new Dictionary<string, ArtifactTargetPaths>(StringComparer.Ordinal);
            var index = 0;

            foreach (JsonElement nodeElement in nodes.EnumerateArray())
            {
                var context = $"graph.json.nodes[{index}]";
                JsonElement node = RequireObject(nodeElement, context);
                var targetId = GetRequiredNonBlankString(node, "id", context);
                var targetKind = GetRequiredNonBlankString(node, "kind", context);
                var targetIdStability = GetRequiredNonBlankString(
                    node,
                    "targetIdStability",
                    context);

                if (!TargetKinds.Contains(targetKind))
                {
                    throw InvalidArtifacts($"{context}.kind '{targetKind}' is not supported.");
                }

                if (!TargetIdStabilities.Contains(targetIdStability))
                {
                    throw InvalidArtifacts(
                        $"{context}.targetIdStability '{targetIdStability}' is not supported.");
                }

                ParsedGraphFilePaths filePaths = ParseGraphFilePaths(node, targetKind, context);
                if (!targets.TryAdd(
                        targetId,
                        new ArtifactTargetPaths(
                            targetId,
                            targetKind,
                            targetIdStability,
                            filePaths.FilePaths,
                            filePaths.HasCompleteDeclarationPaths)))
                {
                    throw InvalidArtifacts($"graph.json contains duplicate node id '{targetId}'.");
                }

                index++;
            }

            return new ArtifactGraph(schemaVersion, targets);
        }
        catch (ScoringException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw InvalidArtifacts("graph.json is not valid JSON.", exception);
        }
    }

    private static ParsedGraphFilePaths ParseGraphFilePaths(
        JsonElement node,
        string targetKind,
        string context)
    {
        if (targetKind is "solution" or "project")
        {
            return new ParsedGraphFilePaths([], true);
        }

        var primaryFilePath = GetOptionalNonBlankString(node, "filePath", context);
        primaryFilePath = primaryFilePath is null ? null : NormalizePath(primaryFilePath);
        if (targetKind == "file")
        {
            return primaryFilePath is null
                ? new ParsedGraphFilePaths([], false)
                : new ParsedGraphFilePaths([primaryFilePath], true);
        }

        if (!node.TryGetProperty("declarations", out JsonElement declarations))
        {
            return primaryFilePath is null
                ? new ParsedGraphFilePaths([], false)
                : new ParsedGraphFilePaths([primaryFilePath], false);
        }

        if (declarations.ValueKind != JsonValueKind.Array)
        {
            throw InvalidArtifacts($"{context}.declarations must be an array.");
        }

        var filePaths = new HashSet<string>(StringComparer.Ordinal);
        var declarationIndex = 0;
        foreach (JsonElement declarationElement in declarations.EnumerateArray())
        {
            var declarationContext = $"{context}.declarations[{declarationIndex}]";
            JsonElement declaration = RequireObject(declarationElement, declarationContext);
            filePaths.Add(NormalizePath(
                GetRequiredNonBlankString(declaration, "filePath", declarationContext)));
            declarationIndex++;
        }

        if (filePaths.Count == 0)
        {
            throw InvalidArtifacts($"{context}.declarations must contain at least one source span.");
        }

        if (primaryFilePath is not null && !filePaths.Contains(primaryFilePath))
        {
            throw InvalidArtifacts(
                $"{context}.filePath '{primaryFilePath}' is not present in declarations.");
        }

        return new ParsedGraphFilePaths(
            filePaths.Order(StringComparer.Ordinal).ToArray(),
            true);
    }

    private static async Task<MetricsSnapshot> ReadMetricsSnapshotAsync(
        string path,
        ScoringInputLimits limits,
        CancellationToken cancellationToken)
    {
        var metrics = new List<ArtifactMetric>();

        try
        {
            var declaredLength = new FileInfo(path).Length;
            if (declaredLength > limits.MaxMetricsBytes)
            {
                throw new InvalidDataException(
                    $"{ArtifactNames.Metrics} is {declaredLength} bytes and exceeds the maximum " +
                    $"of {limits.MaxMetricsBytes} bytes.");
            }

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var boundedStream = new ScoringByteLimitedReadStream(
                stream,
                limits.MaxMetricsBytes,
                ArtifactNames.Metrics);
            using var hashingStream = new HashingReadStream(boundedStream, hash);
            var recordCount = 0;

            await foreach (BoundedInputLine boundedLine in ScoringInputReader.ReadLinesAsync(
                hashingStream,
                ArtifactNames.Metrics,
                limits.MaxMetricLineCharacters,
                limits.MaxMetricLines,
                cancellationToken))
            {
                var line = boundedLine.Text;
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                recordCount++;
                if (recordCount > limits.MaxMetricRecords)
                {
                    throw new InvalidDataException(
                        $"{ArtifactNames.Metrics} exceeds the maximum record count of " +
                        $"{limits.MaxMetricRecords}.");
                }

                metrics.Add(ParseMetric(line, boundedLine.Number, limits.MaxJsonDepth));
            }

            if (!hashingStream.ReachedEnd)
            {
                throw InvalidArtifacts("metrics.ndjson was not read to the end of its scoring snapshot.");
            }

            var sha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();

            return new MetricsSnapshot(metrics, sha256);
        }
        catch (ScoringException)
        {
            throw;
        }
        catch (InvalidDataException exception)
        {
            throw InvalidArtifacts(exception.Message, exception);
        }
        catch (DecoderFallbackException exception)
        {
            throw InvalidArtifacts($"{ArtifactNames.Metrics} is not valid UTF-8.", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw InvalidArtifacts($"Could not read '{ArtifactNames.Metrics}'.", exception);
        }
    }

    private static ArtifactMetric ParseMetric(string json, int lineNumber, int maxJsonDepth)
    {
        var context = $"metrics.ndjson line {lineNumber}";

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = maxJsonDepth });
            JsonElement root = RequireObject(document.RootElement, context);

            var schemaVersion = GetRequiredNonBlankString(root, "schemaVersion", context);
            var metricId = GetRequiredNonBlankString(root, "metricId", context);
            var metricVersion = GetRequiredNonBlankString(root, "metricVersion", context);
            var targetId = GetRequiredNonBlankString(root, "targetId", context);
            var targetKind = GetRequiredNonBlankString(root, "targetKind", context);
            var targetIdStability = GetRequiredNonBlankString(
                root,
                "targetIdStability",
                context);
            var filePath = GetOptionalNonBlankString(root, "filePath", context);

            if (!TargetKinds.Contains(targetKind))
            {
                throw InvalidArtifacts($"{context}.targetKind '{targetKind}' is not supported.");
            }

            if (!TargetIdStabilities.Contains(targetIdStability))
            {
                throw InvalidArtifacts(
                    $"{context}.targetIdStability '{targetIdStability}' is not supported.");
            }

            var valueKind = GetRequiredNonBlankString(root, "valueKind", context);
            var numericValue = ParseMetricValue(root, valueKind, context);

            IReadOnlyList<string> tags = ParseTags(root, context);

            return new ArtifactMetric(
                schemaVersion,
                metricId,
                metricVersion,
                targetId,
                targetKind,
                targetIdStability,
                numericValue,
                filePath is null ? null : NormalizePath(filePath),
                tags);
        }
        catch (ScoringException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw InvalidArtifacts($"{context} is not valid JSON.", exception);
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static void EnsureUniqueMetrics(IReadOnlyList<ArtifactMetric> metrics)
    {
        var seen = new HashSet<(
            string TargetId,
            string MetricId,
            string MetricVersion,
            string TagSignature)>();

        foreach (ArtifactMetric metric in metrics)
        {
            if (!seen.Add((
                    metric.TargetId,
                    metric.MetricId,
                    metric.MetricVersion,
                    metric.TagSignature)))
            {
                throw InvalidArtifacts(
                    $"metrics.ndjson contains duplicate metric '{metric.MetricId}@{metric.MetricVersion}' " +
                    $"for target '{metric.TargetId}' and tags [{string.Join(", ", metric.Tags)}].");
            }
        }
    }

    private static void ValidateMetricTargets(
        IReadOnlyList<ArtifactMetric> metrics,
        ArtifactGraph graph)
    {
        foreach (ArtifactMetric metric in metrics)
        {
            if (!graph.Targets.TryGetValue(metric.TargetId, out ArtifactTargetPaths? target))
            {
                throw InvalidArtifacts(
                    $"graph.json has no node for metric target '{metric.TargetId}'.");
            }

            if (!string.Equals(target.TargetKind, metric.TargetKind, StringComparison.Ordinal))
            {
                throw InvalidArtifacts(
                    $"Metric target '{metric.TargetId}' has targetKind '{metric.TargetKind}', but its " +
                    $"graph node uses kind '{target.TargetKind}'.");
            }

            if (!string.Equals(
                    target.TargetIdStability,
                    metric.TargetIdStability,
                    StringComparison.Ordinal))
            {
                throw InvalidArtifacts(
                    $"Metric target '{metric.TargetId}' has targetIdStability " +
                    $"'{metric.TargetIdStability}', but its graph node uses " +
                    $"'{target.TargetIdStability}'.");
            }

            if (metric.FilePath is not null &&
                target.FilePaths.Count > 0 &&
                !target.FilePaths.Contains(metric.FilePath, StringComparer.Ordinal))
            {
                throw InvalidArtifacts(
                    $"Metric target '{metric.TargetId}' uses filePath '{metric.FilePath}', which is " +
                    "not present in its graph declarations.");
            }
        }
    }

    private static string[] ParseTags(JsonElement metric, string context)
    {
        if (!metric.TryGetProperty("tags", out JsonElement tagsElement))
        {
            return [];
        }

        if (tagsElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidArtifacts($"{context}.tags must be an array of non-empty strings.");
        }

        var tags = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (JsonElement tagElement in tagsElement.EnumerateArray())
        {
            if (tagElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(tagElement.GetString()))
            {
                throw InvalidArtifacts($"{context}.tags[{index}] must be a non-empty string.");
            }

            var tag = tagElement.GetString()!;
            if (!tags.Add(tag))
            {
                throw InvalidArtifacts($"{context}.tags contains duplicate tag '{tag}'.");
            }

            index++;
        }

        return tags.Order(StringComparer.Ordinal).ToArray();
    }

    private static double? ParseMetricValue(
        JsonElement metric,
        string valueKind,
        string context)
    {
        var hasNumericValue = metric.TryGetProperty("numericValue", out JsonElement numericValue);
        var hasStringValue = metric.TryGetProperty("stringValue", out JsonElement stringValue);
        var hasBoolValue = metric.TryGetProperty("boolValue", out JsonElement boolValue);
        var valueFieldCount = (hasNumericValue ? 1 : 0) +
            (hasStringValue ? 1 : 0) +
            (hasBoolValue ? 1 : 0);

        if (valueFieldCount != 1)
        {
            throw InvalidArtifacts(
                $"{context} must contain exactly one of numericValue, stringValue, or boolValue.");
        }

        switch (valueKind)
        {
            case "integer":
            case "number":
                if (!hasNumericValue || numericValue.ValueKind != JsonValueKind.Number)
                {
                    throw InvalidArtifacts(
                        $"{context}.valueKind '{valueKind}' requires one finite numericValue.");
                }

                if (valueKind == "integer" && !IsMathematicalInteger(numericValue))
                {
                    throw InvalidArtifacts(
                        $"{context}.valueKind 'integer' requires an integral numericValue.");
                }

                if (!numericValue.TryGetDouble(out var parsedNumericValue) ||
                    !double.IsFinite(parsedNumericValue))
                {
                    throw InvalidArtifacts(
                        $"{context}.numericValue must be representable as a finite scoring number.");
                }

                return parsedNumericValue;

            case "string":
                if (!hasStringValue || stringValue.ValueKind != JsonValueKind.String)
                {
                    throw InvalidArtifacts(
                        $"{context}.valueKind 'string' requires one stringValue.");
                }

                return null;

            case "boolean":
                if (!hasBoolValue || boolValue.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw InvalidArtifacts(
                        $"{context}.valueKind 'boolean' requires one boolValue.");
                }

                return null;

            default:
                throw InvalidArtifacts(
                    $"{context}.valueKind '{valueKind}' is not supported. " +
                    "Expected integer, number, string, or boolean.");
        }
    }

    private static bool IsMathematicalInteger(JsonElement number)
    {
        var raw = number.GetRawText();
        var exponentSeparator = raw.IndexOfAny(['e', 'E']);
        ReadOnlySpan<char> significand = exponentSeparator < 0
            ? raw.AsSpan()
            : raw.AsSpan(0, exponentSeparator);
        BigInteger exponent = BigInteger.Zero;

        if (exponentSeparator >= 0 &&
            !BigInteger.TryParse(
                raw.AsSpan(exponentSeparator + 1),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out exponent))
        {
            return false;
        }

        var decimalSeparator = significand.IndexOf('.');
        var fractionalDigits = decimalSeparator < 0
            ? 0
            : significand.Length - decimalSeparator - 1;
        BigInteger scale = fractionalDigits - exponent;

        if (scale <= 0)
        {
            return true;
        }

        var isZero = true;
        var digitCount = 0;
        foreach (var character in significand)
        {
            if (character is '.' or '-')
            {
                continue;
            }

            digitCount++;
            isZero &= character == '0';
        }

        if (isZero)
        {
            return true;
        }

        if (scale > digitCount)
        {
            return false;
        }

        var requiredTrailingZeros = (int)scale;
        var checkedDigits = 0;
        for (var index = significand.Length - 1; index >= 0 && checkedDigits < requiredTrailingZeros; index--)
        {
            var character = significand[index];
            if (character == '.')
            {
                continue;
            }

            if (character != '0')
            {
                return false;
            }

            checkedDigits++;
        }

        return checkedDigits == requiredTrailingZeros;
    }

    private static void EnsureRegularDirectory(string artifactDirectory)
    {
        if (!Directory.Exists(artifactDirectory))
        {
            return;
        }

        if ((File.GetAttributes(artifactDirectory) & FileAttributes.ReparsePoint) != 0)
        {
            throw InvalidArtifacts(
                "The artifact directory must be a regular directory, not a symbolic link or reparse point.");
        }
    }

    private static void EnsureFileExists(string path, string artifactName)
    {
        if (!File.Exists(path))
        {
            throw InvalidArtifacts($"Required artifact '{artifactName}' does not exist.");
        }

        if (!RegularFile.IsRegularFile(path))
        {
            throw InvalidArtifacts(
                $"Required artifact must be a regular file, not a symbolic link, reparse point, or special file: {artifactName}");
        }
    }

    private static async Task<byte[]> ReadSnapshotAsync(
        string path,
        string artifactName,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        try
        {
            return await ScoringInputReader.ReadFileBytesAsync(
                path,
                maxBytes,
                artifactName,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw InvalidArtifacts(exception.Message, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw InvalidArtifacts($"Could not read '{artifactName}'.", exception);
        }
    }

    private static JsonElement RequireObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidArtifacts($"{context} must contain a JSON object.");
        }

        return element;
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement property))
        {
            throw InvalidArtifacts($"Missing required property '{context}.{name}'.");
        }

        return property;
    }

    private static string GetRequiredNonBlankString(JsonElement element, string name, string context)
    {
        JsonElement property = GetRequiredProperty(element, name, context);
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw InvalidArtifacts($"{context}.{name} must be a non-empty string.");
        }

        return property.GetString()!;
    }

    private static string? GetOptionalNonBlankString(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement property))
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw InvalidArtifacts($"{context}.{name} must be a non-empty string when provided.");
        }

        return property.GetString();
    }

    private static int GetRequiredNonNegativeInteger(JsonElement element, string name, string context)
    {
        JsonElement property = GetRequiredProperty(element, name, context);
        if (property.ValueKind != JsonValueKind.Number ||
            !property.TryGetInt32(out var value) ||
            value < 0)
        {
            throw InvalidArtifacts($"{context}.{name} must be a non-negative 32-bit integer.");
        }

        return value;
    }

    private static bool GetRequiredBoolean(JsonElement element, string name, string context)
    {
        JsonElement property = GetRequiredProperty(element, name, context);
        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw InvalidArtifacts($"{context}.{name} must be a boolean.");
        }

        return property.GetBoolean();
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Replace('\\', '/');

        return normalized.StartsWith("./", StringComparison.Ordinal) ? normalized[2..] : normalized;
    }

    private static ScoringException InvalidArtifacts(string message, Exception? innerException = null)
    {
        return innerException is null
            ? new ScoringException(ScoringFailureKind.InvalidArtifacts, message)
            : new ScoringException(ScoringFailureKind.InvalidArtifacts, message, innerException);
    }

    private sealed record ParsedGraphFilePaths(
        IReadOnlyList<string> FilePaths,
        bool HasCompleteDeclarationPaths);

    private sealed record ArtifactManifest(string SchemaVersion, string Mode);
}
