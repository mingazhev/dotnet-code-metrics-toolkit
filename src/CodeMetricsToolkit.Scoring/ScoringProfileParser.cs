using System.Text.Json;

namespace CodeMetricsToolkit.Scoring;

internal static class ScoringProfileParser
{
    private static readonly HashSet<string> TargetKinds =
        new(["file", "type", "member"], StringComparer.Ordinal);
    private static readonly HashSet<string> AnalysisModes =
        new(["syntax", "semantic", "partial_semantic"], StringComparer.Ordinal);
    private static readonly HashSet<string> TargetIdStabilities =
        new(["semantic", "syntax_fallback", "line_fallback"], StringComparer.Ordinal);

    public static ParsedScoringProfile Parse(string json, int maxJsonDepth)
    {
        ArgumentNullException.ThrowIfNull(json);

        try
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = maxJsonDepth });
            JsonElement root = RequireObject(document.RootElement, "profile");
            EnsureAllowedProperties(
                root,
                "profile",
                "schemaVersion",
                "id",
                "version",
                "artifactContractVersion",
                "allowedAnalysisModes",
                "allowedTargetIdStabilities",
                "selectors",
                "primary",
                "operations");

            var schemaVersion = GetRequiredString(root, "schemaVersion", "profile");
            if (!string.Equals(schemaVersion, ScoringProfileContract.Current, StringComparison.Ordinal))
            {
                throw InvalidProfile(
                    $"Unsupported scoring profile schemaVersion '{schemaVersion}'. " +
                    $"Expected '{ScoringProfileContract.Current}'.");
            }

            IReadOnlyList<string> allowedAnalysisModes = ParseRequiredEnumArray(
                root,
                "allowedAnalysisModes",
                AnalysisModes);
            IReadOnlyList<string> allowedTargetIdStabilities = ParseRequiredEnumArray(
                root,
                "allowedTargetIdStabilities",
                TargetIdStabilities);

            var id = GetRequiredNonBlankString(root, "id", "profile");
            var version = GetRequiredNonBlankString(root, "version", "profile");
            if (!IsThreePartVersion(version))
            {
                throw InvalidProfile(
                    "profile.version must use a numeric major.minor.patch format, for example '1.2.0'.");
            }

            var artifactContractVersion = GetRequiredNonBlankString(
                root,
                "artifactContractVersion",
                "profile");
            if (!string.Equals(
                    artifactContractVersion,
                    ScoringProfileContract.SupportedArtifactContractVersion,
                    StringComparison.Ordinal))
            {
                throw InvalidProfile(
                    $"Unsupported profile.artifactContractVersion '{artifactContractVersion}'. " +
                    $"Expected '{ScoringProfileContract.SupportedArtifactContractVersion}'.");
            }

            SelectionOptions selectors = ParseSelectors(GetRequiredProperty(root, "selectors", "profile"));
            ScoringPrimaryMetric primary = ParsePrimary(GetRequiredProperty(root, "primary", "profile"));
            IReadOnlyList<ParsedScoringOperation> operations = ParseOperations(
                GetRequiredProperty(root, "operations", "profile"));

            ValidateOperationGraph(operations, primary);

            return new ParsedScoringProfile(
                schemaVersion,
                id,
                version,
                artifactContractVersion,
                allowedAnalysisModes,
                allowedTargetIdStabilities,
                selectors,
                primary,
                operations);
        }
        catch (ScoringException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ScoringException(
                ScoringFailureKind.InvalidProfile,
                "The scoring profile is not valid JSON.",
                exception);
        }
    }

    private static SelectionOptions ParseSelectors(JsonElement element)
    {
        JsonElement selectors = RequireObject(element, "selectors");
        EnsureAllowedProperties(
            selectors,
            "selectors",
            "includeFilePaths",
            "excludeFilePaths",
            "requireTrusted",
            "minTargetCount");

        IReadOnlyList<string> includeFilePaths = GetStringArray(
            selectors,
            "includeFilePaths",
            "selectors",
            []);
        IReadOnlyList<string> excludeFilePaths = GetStringArray(
            selectors,
            "excludeFilePaths",
            "selectors",
            []);
        var requireTrusted = GetOptionalBoolean(selectors, "requireTrusted", "selectors", true);
        var minTargetCount = GetOptionalInteger(selectors, "minTargetCount", "selectors", 1);

        if (minTargetCount < 1)
        {
            throw InvalidProfile("selectors.minTargetCount must be at least 1.");
        }

        EnsureDistinct(includeFilePaths, "selectors.includeFilePaths");
        EnsureDistinct(excludeFilePaths, "selectors.excludeFilePaths");

        return new SelectionOptions(
            includeFilePaths,
            excludeFilePaths,
            requireTrusted,
            minTargetCount);
    }

    private static ScoringPrimaryMetric ParsePrimary(JsonElement element)
    {
        JsonElement primary = RequireObject(element, "primary");
        EnsureAllowedProperties(primary, "primary", "key", "direction");

        var key = GetRequiredNonBlankString(primary, "key", "primary");
        var direction = GetRequiredString(primary, "direction", "primary");

        if (direction is not ("minimize" or "maximize"))
        {
            throw InvalidProfile("primary.direction must be either 'minimize' or 'maximize'.");
        }

        return new ScoringPrimaryMetric
        {
            Key = key,
            Direction = direction
        };
    }

    private static List<ParsedScoringOperation> ParseOperations(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProfile("profile.operations must be an array.");
        }

        var operations = new List<ParsedScoringOperation>();
        var index = 0;

        foreach (JsonElement operationElement in element.EnumerateArray())
        {
            JsonElement operation = RequireObject(operationElement, $"operations[{index}]");
            var operationName = GetRequiredString(operation, "operation", $"operations[{index}]");

            operations.Add(operationName switch
            {
                "thresholdDebt" => ParseThresholdDebt(operation, index),
                "weightedSum" => ParseWeightedSum(operation, index),
                _ => throw InvalidProfile(
                    $"Unknown operation '{operationName}' at operations[{index}]. " +
                    "Supported operations are 'thresholdDebt' and 'weightedSum'.")
            });

            index++;
        }

        if (operations.Count == 0)
        {
            throw InvalidProfile("profile.operations must contain at least one operation.");
        }

        return operations;
    }

    private static ThresholdDebtOperation ParseThresholdDebt(JsonElement operation, int index)
    {
        var context = $"operations[{index}]";
        EnsureAllowedProperties(operation, context, "operation", "targetKind", "thresholds", "outputs");

        var targetKind = GetRequiredString(operation, "targetKind", context);
        if (!TargetKinds.Contains(targetKind))
        {
            throw InvalidProfile(
                $"{context}.targetKind '{targetKind}' is not supported. " +
                "Expected a source-linked target kind: file, type, or member.");
        }

        IReadOnlyList<MetricThreshold> thresholds = ParseThresholds(
            GetRequiredProperty(operation, "thresholds", context),
            context);
        ThresholdDebtOutputs outputs = ParseThresholdOutputs(
            GetRequiredProperty(operation, "outputs", context),
            context);

        return new ThresholdDebtOperation(targetKind, thresholds, outputs);
    }

    private static List<MetricThreshold> ParseThresholds(JsonElement element, string operationContext)
    {
        var context = $"{operationContext}.thresholds";
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProfile($"{context} must be an array.");
        }

        var thresholds = new List<MetricThreshold>();
        var references = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (JsonElement thresholdElement in element.EnumerateArray())
        {
            var thresholdContext = $"{context}[{index}]";
            JsonElement threshold = RequireObject(thresholdElement, thresholdContext);
            EnsureAllowedProperties(
                threshold,
                thresholdContext,
                "metricId",
                "metricVersion",
                "maximum",
                "weight",
                "tags");

            var metricId = GetRequiredNonBlankString(threshold, "metricId", thresholdContext);
            var metricVersion = GetRequiredNonBlankString(threshold, "metricVersion", thresholdContext);
            if (!IsThreePartVersion(metricVersion))
            {
                throw InvalidProfile(
                    $"{thresholdContext}.metricVersion must use numeric major.minor.patch format.");
            }

            var maximum = GetRequiredFiniteDouble(threshold, "maximum", thresholdContext);
            var weight = GetOptionalFiniteDouble(threshold, "weight", thresholdContext, 1d);
            IReadOnlyList<string> tags = GetStringArray(
                threshold,
                "tags",
                thresholdContext,
                []);

            if (weight <= 0)
            {
                throw InvalidProfile($"{thresholdContext}.weight must be greater than 0.");
            }

            EnsureDistinct(tags, $"{thresholdContext}.tags");
            var normalizedTags = tags.Order(StringComparer.Ordinal).ToArray();
            var parsed = new MetricThreshold(metricId, metricVersion, maximum, weight, normalizedTags);
            if (!references.Add(parsed.Signature))
            {
                throw InvalidProfile($"{context} contains duplicate metric '{parsed.Reference}'.");
            }

            thresholds.Add(parsed);
            index++;
        }

        if (thresholds.Count == 0)
        {
            throw InvalidProfile($"{context} must contain at least one metric threshold.");
        }

        return thresholds;
    }

    private static ThresholdDebtOutputs ParseThresholdOutputs(JsonElement element, string operationContext)
    {
        var context = $"{operationContext}.outputs";
        JsonElement outputs = RequireObject(element, context);
        EnsureAllowedProperties(
            outputs,
            context,
            "gapSum",
            "squaredGapSum",
            "violatingTargetCount",
            "maxTargetGap");

        return new ThresholdDebtOutputs(
            GetRequiredNonBlankString(outputs, "gapSum", context),
            GetRequiredNonBlankString(outputs, "squaredGapSum", context),
            GetRequiredNonBlankString(outputs, "violatingTargetCount", context),
            GetRequiredNonBlankString(outputs, "maxTargetGap", context));
    }

    private static WeightedSumOperation ParseWeightedSum(JsonElement operation, int index)
    {
        var context = $"operations[{index}]";
        EnsureAllowedProperties(operation, context, "operation", "terms", "output");

        JsonElement termsElement = GetRequiredProperty(operation, "terms", context);
        if (termsElement.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProfile($"{context}.terms must be an array.");
        }

        var terms = new List<WeightedTerm>();
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var termIndex = 0;

        foreach (JsonElement termElement in termsElement.EnumerateArray())
        {
            var termContext = $"{context}.terms[{termIndex}]";
            JsonElement term = RequireObject(termElement, termContext);
            EnsureAllowedProperties(term, termContext, "key", "weight");

            var key = GetRequiredNonBlankString(term, "key", termContext);
            var weight = GetRequiredFiniteDouble(term, "weight", termContext);

            if (!keys.Add(key))
            {
                throw InvalidProfile($"{context}.terms contains duplicate key '{key}'.");
            }

            terms.Add(new WeightedTerm(key, weight));
            termIndex++;
        }

        if (terms.Count == 0)
        {
            throw InvalidProfile($"{context}.terms must contain at least one term.");
        }

        return new WeightedSumOperation(
            terms,
            GetRequiredNonBlankString(operation, "output", context));
    }

    private static void ValidateOperationGraph(
        IReadOnlyList<ParsedScoringOperation> operations,
        ScoringPrimaryMetric primary)
    {
        var availableOutputs = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < operations.Count; index++)
        {
            ParsedScoringOperation operation = operations[index];

            switch (operation)
            {
                case ThresholdDebtOperation thresholdDebt:
                    foreach (var output in thresholdDebt.Outputs.All)
                    {
                        RegisterOutput(availableOutputs, output, index);
                    }

                    break;

                case WeightedSumOperation weightedSum:
                    foreach (WeightedTerm term in weightedSum.Terms)
                    {
                        if (!availableOutputs.Contains(term.Key))
                        {
                            throw InvalidProfile(
                                $"operations[{index}] depends on '{term.Key}', which is not an output " +
                                "of an earlier operation. Operations are evaluated in declaration order.");
                        }
                    }

                    RegisterOutput(availableOutputs, weightedSum.Output, index);
                    break;
            }
        }

        if (!availableOutputs.Contains(primary.Key))
        {
            throw InvalidProfile(
                $"primary.key '{primary.Key}' is not produced by any operation.");
        }
    }

    private static void RegisterOutput(HashSet<string> outputs, string output, int operationIndex)
    {
        if (!outputs.Add(output))
        {
            throw InvalidProfile(
                $"Duplicate output key '{output}' at operations[{operationIndex}]. " +
                "Every numeric output key must be unique.");
        }
    }

    private static JsonElement RequireObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw InvalidProfile($"{context} must be a JSON object.");
        }

        return element;
    }

    private static JsonElement GetRequiredProperty(JsonElement element, string name, string context)
    {
        if (!element.TryGetProperty(name, out JsonElement property))
        {
            throw InvalidProfile($"Missing required property '{context}.{name}'.");
        }

        return property;
    }

    private static string GetRequiredString(JsonElement element, string name, string context)
    {
        JsonElement property = GetRequiredProperty(element, name, context);
        if (property.ValueKind != JsonValueKind.String)
        {
            throw InvalidProfile($"{context}.{name} must be a string.");
        }

        return property.GetString()!;
    }

    private static string GetRequiredNonBlankString(JsonElement element, string name, string context)
    {
        var value = GetRequiredString(element, name, context);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw InvalidProfile($"{context}.{name} must not be empty or whitespace.");
        }

        return value;
    }

    private static IReadOnlyList<string> GetStringArray(
        JsonElement element,
        string name,
        string context,
        IReadOnlyList<string> defaultValue)
    {
        if (!element.TryGetProperty(name, out JsonElement property))
        {
            return defaultValue;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            throw InvalidProfile($"{context}.{name} must be an array of strings.");
        }

        var values = new List<string>();
        var index = 0;

        foreach (JsonElement item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw InvalidProfile($"{context}.{name}[{index}] must be a non-empty string.");
            }

            values.Add(item.GetString()!);
            index++;
        }

        return values;
    }

    private static string[] ParseRequiredEnumArray(
        JsonElement element,
        string name,
        HashSet<string> allowedValues)
    {
        IReadOnlyList<string> values = GetStringArray(element, name, "profile", []);
        if (values.Count == 0)
        {
            throw InvalidProfile($"profile.{name} must contain at least one value.");
        }

        EnsureDistinct(values, $"profile.{name}");

        foreach (var value in values)
        {
            if (!allowedValues.Contains(value))
            {
                throw InvalidProfile(
                    $"profile.{name} contains unsupported value '{value}'.");
            }
        }

        return values.Order(StringComparer.Ordinal).ToArray();
    }

    private static bool GetOptionalBoolean(
        JsonElement element,
        string name,
        string context,
        bool defaultValue)
    {
        if (!element.TryGetProperty(name, out JsonElement property))
        {
            return defaultValue;
        }

        if (property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw InvalidProfile($"{context}.{name} must be a boolean.");
        }

        return property.GetBoolean();
    }

    private static int GetOptionalInteger(
        JsonElement element,
        string name,
        string context,
        int defaultValue)
    {
        if (!element.TryGetProperty(name, out JsonElement property))
        {
            return defaultValue;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetInt32(out var value))
        {
            throw InvalidProfile($"{context}.{name} must be a 32-bit integer.");
        }

        return value;
    }

    private static double GetRequiredFiniteDouble(JsonElement element, string name, string context)
    {
        JsonElement property = GetRequiredProperty(element, name, context);

        return GetFiniteDouble(property, $"{context}.{name}");
    }

    private static double GetOptionalFiniteDouble(
        JsonElement element,
        string name,
        string context,
        double defaultValue)
    {
        return element.TryGetProperty(name, out JsonElement property)
            ? GetFiniteDouble(property, $"{context}.{name}")
            : defaultValue;
    }

    private static double GetFiniteDouble(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Number ||
            !element.TryGetDouble(out var value) ||
            !double.IsFinite(value))
        {
            throw InvalidProfile($"{context} must be a finite number.");
        }

        return value;
    }

    private static void EnsureDistinct(IReadOnlyList<string> values, string context)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (!seen.Add(value))
            {
                throw InvalidProfile($"{context} contains duplicate value '{value}'.");
            }
        }
    }

    private static bool IsThreePartVersion(string value)
    {
        var parts = value.Split('.');

        return parts.Length == 3 && parts.All(part =>
            part.Length > 0 && part.All(character => character is >= '0' and <= '9'));
    }

    private static void EnsureAllowedProperties(
        JsonElement element,
        string context,
        params string[] allowedProperties)
    {
        var allowed = new HashSet<string>(allowedProperties, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                throw InvalidProfile($"Duplicate JSON property '{context}.{property.Name}'.");
            }

            if (!allowed.Contains(property.Name))
            {
                throw InvalidProfile($"Unknown property '{context}.{property.Name}'.");
            }
        }
    }

    private static ScoringException InvalidProfile(string message)
    {
        return new ScoringException(ScoringFailureKind.InvalidProfile, message);
    }
}
