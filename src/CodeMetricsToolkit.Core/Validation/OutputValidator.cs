using System.Text;
using System.Text.Json;
using CodeMetricsToolkit.Abstractions;

namespace CodeMetricsToolkit.Core.Validation;

public static class OutputValidator
{
    private static readonly IReadOnlyDictionary<string, string> JsonArtifacts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ArtifactNames.Manifest] = "manifest.schema.json",
            [ArtifactNames.Summary] = "summary.schema.json",
            [ArtifactNames.Graph] = "graph.schema.json"
        };

    private static readonly IReadOnlyDictionary<string, string> NdjsonArtifacts =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ArtifactNames.Metrics] = "metric-result.schema.json",
            [ArtifactNames.Chunks] = "chunk.schema.json",
            [ArtifactNames.Diagnostics] = "diagnostic.schema.json"
        };

    public static OutputValidationResult Validate(string artifactDirectory, CancellationToken cancellationToken)
    {
        return Validate(artifactDirectory, ValidationInputLimits.Default, cancellationToken);
    }

    internal static OutputValidationResult Validate(
        string artifactDirectory,
        ValidationInputLimits limits,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
        ArgumentNullException.ThrowIfNull(limits);
        limits.Validate();
        cancellationToken.ThrowIfCancellationRequested();

        var errors = new List<string>();

        if (!Directory.Exists(artifactDirectory))
        {
            return new OutputValidationResult
            {
                IsValid = false,
                Errors = [$"Artifact directory does not exist: {artifactDirectory}"]
            };
        }

        foreach (var artifactName in JsonArtifacts.Keys.Concat(NdjsonArtifacts.Keys))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRequiredArtifact(artifactDirectory, artifactName, errors);
        }

        if (errors.Count > 0)
        {
            return CreateResult(errors);
        }

        var documents = new Dictionary<string, JsonElement>(StringComparer.Ordinal);

        foreach ((var artifactName, var schemaName) in JsonArtifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            JsonElement? document = ValidateJson(
                Path.Combine(artifactDirectory, artifactName),
                schemaName,
                limits,
                errors);

            if (document is not null)
            {
                documents.Add(artifactName, document.Value);
            }
        }

        var manifestSchemaVersion = documents.TryGetValue(ArtifactNames.Manifest, out JsonElement manifest)
            ? ArtifactContractInvariants.ReadStringProperty(manifest, "schemaVersion")
            : null;

        var ndjsonMetadata = new Dictionary<string, NdjsonArtifactMetadata>(StringComparer.Ordinal);

        foreach ((var artifactName, var schemaName) in NdjsonArtifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NdjsonArtifactMetadata metadata = ValidateNdjson(
                Path.Combine(artifactDirectory, artifactName),
                schemaName,
                manifestSchemaVersion,
                limits,
                errors,
                cancellationToken);

            ndjsonMetadata.Add(artifactName, metadata);
        }

        ArtifactContractInvariants.Validate(documents, ndjsonMetadata, errors);

        return CreateResult(errors);
    }

    private static void ValidateRequiredArtifact(
        string artifactDirectory,
        string artifactName,
        List<string> errors)
    {
        var artifactPath = Path.Combine(artifactDirectory, artifactName);

        if (!File.Exists(artifactPath))
        {
            errors.Add($"Missing required artifact: {artifactName}");
            return;
        }

        try
        {
            if ((File.GetAttributes(artifactPath) & FileAttributes.ReparsePoint) != 0)
            {
                errors.Add(
                    $"Required artifact must be a regular file, not a symbolic link or reparse point: {artifactName}");
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Required artifact metadata could not be read for {artifactName}: {exception.Message}");
        }
    }

    private static JsonElement? ValidateJson(
        string artifactPath,
        string schemaName,
        ValidationInputLimits limits,
        List<string> errors)
    {
        var artifactName = Path.GetFileName(artifactPath);

        try
        {
            var json = ValidationInputReader.ReadJsonBytes(
                artifactPath,
                limits.MaxJsonArtifactBytes);
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
            JsonElement root = document.RootElement.Clone();
            ValidateAgainstSchema(root, artifactName, schemaName, null, errors);

            return root;
        }
        catch (JsonException exception)
        {
            errors.Add($"{artifactName} is invalid JSON: {exception.Message}");
        }
        catch (InvalidDataException exception)
        {
            errors.Add(exception.Message);
        }
        catch (IOException exception)
        {
            errors.Add($"{artifactName} could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            errors.Add($"{artifactName} could not be read: {exception.Message}");
        }

        return null;
    }

    private static NdjsonArtifactMetadata ValidateNdjson(
        string artifactPath,
        string schemaName,
        string? manifestSchemaVersion,
        ValidationInputLimits limits,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        var artifactName = Path.GetFileName(artifactPath);
        long recordCount = 0;
        var lineNumber = 0;
        var targetReferences = new List<ArtifactTargetReference>();
        var seenTargetReferences = new HashSet<(
            string TargetId,
            string TargetKind,
            string TargetIdStability)>();

        try
        {
            foreach (BoundedTextLine boundedLine in ValidationInputReader.ReadNdjsonLines(
                artifactPath,
                limits,
                cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                lineNumber = boundedLine.Number;
                var line = boundedLine.Text;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                recordCount++;
                if (recordCount > limits.MaxNdjsonRecords)
                {
                    throw new InvalidDataException(
                        $"{artifactName} exceeds the maximum record count of " +
                        $"{limits.MaxNdjsonRecords}.");
                }

                try
                {
                    using var document = JsonDocument.Parse(
                        line,
                        new JsonDocumentOptions { MaxDepth = limits.MaxJsonDepth });
                    JsonElement root = document.RootElement;
                    ValidateAgainstSchema(root, artifactName, schemaName, lineNumber, errors);
                    ArtifactContractInvariants.ValidateSchemaVersion(
                        root,
                        artifactName,
                        lineNumber,
                        manifestSchemaVersion,
                        errors);
                    CollectTargetReference(
                        root,
                        artifactName,
                        lineNumber,
                        targetReferences,
                        seenTargetReferences);
                }
                catch (JsonException exception)
                {
                    errors.Add($"{artifactName}:{lineNumber} is invalid JSON: {exception.Message}");
                }
            }
        }
        catch (InvalidDataException exception)
        {
            errors.Add(exception.Message);
        }
        catch (DecoderFallbackException)
        {
            errors.Add($"{artifactName} is not valid UTF-8.");
        }
        catch (IOException exception)
        {
            errors.Add($"{artifactName} could not be read: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            errors.Add($"{artifactName} could not be read: {exception.Message}");
        }

        return new NdjsonArtifactMetadata(recordCount, targetReferences);
    }

    private static void CollectTargetReference(
        JsonElement record,
        string artifactName,
        int lineNumber,
        List<ArtifactTargetReference> targetReferences,
        HashSet<(string TargetId, string TargetKind, string TargetIdStability)> seenTargetReferences)
    {
        if (!string.Equals(artifactName, ArtifactNames.Metrics, StringComparison.Ordinal) &&
            !string.Equals(artifactName, ArtifactNames.Chunks, StringComparison.Ordinal))
        {
            return;
        }

        var targetId = ArtifactContractInvariants.ReadStringProperty(record, "targetId");
        var targetKind = ArtifactContractInvariants.ReadStringProperty(record, "targetKind");
        var targetIdStability = ArtifactContractInvariants.ReadStringProperty(
            record,
            "targetIdStability");

        if (targetId is null ||
            targetKind is null ||
            targetIdStability is null ||
            !seenTargetReferences.Add((targetId, targetKind, targetIdStability)))
        {
            return;
        }

        targetReferences.Add(new ArtifactTargetReference(
            lineNumber,
            targetId,
            targetKind,
            targetIdStability));
    }

    private static void ValidateAgainstSchema(
        JsonElement instance,
        string artifactName,
        string schemaName,
        int? lineNumber,
        List<string> errors)
    {
        IReadOnlyList<string> schemaErrors;

        try
        {
            schemaErrors = EmbeddedSchemaValidator.Validate(instance, schemaName);
        }
        catch (InvalidOperationException exception)
        {
            errors.Add($"Cannot validate {artifactName}: {exception.Message}");
            return;
        }

        if (schemaErrors.Count == 0)
        {
            return;
        }

        var location = lineNumber is null
            ? artifactName
            : $"{artifactName}:{lineNumber.Value}";

        errors.Add(
            $"{location} does not conform to {schemaName}: {string.Join("; ", schemaErrors)}");
    }

    private static OutputValidationResult CreateResult(List<string> errors)
    {
        return new OutputValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}
