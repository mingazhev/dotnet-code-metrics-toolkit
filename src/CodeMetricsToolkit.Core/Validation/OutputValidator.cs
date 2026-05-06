using System.Text.Json;
using CodeMetricsToolkit.Abstractions;

namespace CodeMetricsToolkit.Core.Validation;

public static class OutputValidator
{
    private static readonly string[] MandatoryArtifacts =
    [
        ArtifactNames.Manifest,
        ArtifactNames.Summary,
        ArtifactNames.Metrics,
        ArtifactNames.Graph,
        ArtifactNames.Chunks,
        ArtifactNames.Diagnostics
    ];

    public static OutputValidationResult Validate(string artifactDirectory, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactDirectory);
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

        foreach (string artifactName in MandatoryArtifacts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string artifactPath = Path.Combine(artifactDirectory, artifactName);

            if (!File.Exists(artifactPath))
            {
                errors.Add($"Missing required artifact: {artifactName}");
            }
        }

        if (errors.Count > 0)
        {
            return CreateResult(errors);
        }

        ValidateJson(Path.Combine(artifactDirectory, ArtifactNames.Manifest), errors);
        ValidateJson(Path.Combine(artifactDirectory, ArtifactNames.Summary), errors);
        ValidateJson(Path.Combine(artifactDirectory, ArtifactNames.Graph), errors);
        ValidateNdjson(Path.Combine(artifactDirectory, ArtifactNames.Metrics), errors, cancellationToken);
        ValidateNdjson(Path.Combine(artifactDirectory, ArtifactNames.Chunks), errors, cancellationToken);
        ValidateNdjson(Path.Combine(artifactDirectory, ArtifactNames.Diagnostics), errors, cancellationToken);

        return CreateResult(errors);
    }

    private static void ValidateJson(string artifactPath, List<string> errors)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(artifactPath));
            _ = document.RootElement.ValueKind;
        }
        catch (JsonException exception)
        {
            errors.Add($"{Path.GetFileName(artifactPath)} is invalid JSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            errors.Add($"{Path.GetFileName(artifactPath)} could not be read: {exception.Message}");
        }
    }

    private static void ValidateNdjson(
        string artifactPath,
        List<string> errors,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (string line in File.ReadLines(artifactPath))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                using JsonDocument document = JsonDocument.Parse(line);
                _ = document.RootElement.ValueKind;
            }
        }
        catch (JsonException exception)
        {
            errors.Add($"{Path.GetFileName(artifactPath)} is invalid NDJSON: {exception.Message}");
        }
        catch (IOException exception)
        {
            errors.Add($"{Path.GetFileName(artifactPath)} could not be read: {exception.Message}");
        }
    }

    private static OutputValidationResult CreateResult(IReadOnlyList<string> errors)
    {
        return new OutputValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }
}
