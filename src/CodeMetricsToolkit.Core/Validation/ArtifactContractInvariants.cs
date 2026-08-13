using System.Text.Json;
using CodeMetricsToolkit.Abstractions;

namespace CodeMetricsToolkit.Core.Validation;

internal sealed record ArtifactTargetReference(
    int LineNumber,
    string TargetId,
    string TargetKind,
    string TargetIdStability);

internal sealed record NdjsonArtifactMetadata(
    long RecordCount,
    IReadOnlyList<ArtifactTargetReference> TargetReferences);

internal static class ArtifactContractInvariants
{
    private static readonly IReadOnlyDictionary<string, string> ManifestArtifactProperties =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["summary"] = ArtifactNames.Summary,
            ["metrics"] = ArtifactNames.Metrics,
            ["graph"] = ArtifactNames.Graph,
            ["chunks"] = ArtifactNames.Chunks,
            ["diagnostics"] = ArtifactNames.Diagnostics
        };

    public static void Validate(
        IReadOnlyDictionary<string, JsonElement> documents,
        IReadOnlyDictionary<string, NdjsonArtifactMetadata> ndjsonMetadata,
        List<string> errors)
    {
        var manifestSchemaVersion = documents.TryGetValue(ArtifactNames.Manifest, out JsonElement manifest)
            ? ReadStringProperty(manifest, "schemaVersion")
            : null;

        ValidateManifest(documents, errors);
        ValidateJsonArtifactVersions(documents, manifestSchemaVersion, errors);
        ValidateRootPathConsistency(documents, errors);

        GraphMetadata? graphMetadata = ValidateGraph(documents, errors);
        ValidateSummaryCounts(documents, ndjsonMetadata, graphMetadata, errors);
        ValidateTargetReferences(ndjsonMetadata, graphMetadata, errors);
    }

    public static void ValidateSchemaVersion(
        JsonElement instance,
        string artifactName,
        int? lineNumber,
        string? manifestSchemaVersion,
        List<string> errors)
    {
        if (manifestSchemaVersion is null)
        {
            return;
        }

        var artifactSchemaVersion = ReadStringProperty(instance, "schemaVersion");

        if (artifactSchemaVersion is null ||
            string.Equals(artifactSchemaVersion, manifestSchemaVersion, StringComparison.Ordinal))
        {
            return;
        }

        var location = lineNumber is null
            ? artifactName
            : $"{artifactName}:{lineNumber.Value}";

        errors.Add(
            $"{location} schemaVersion '{artifactSchemaVersion}' does not match " +
            $"manifest schemaVersion '{manifestSchemaVersion}'.");
    }

    public static string? ReadStringProperty(JsonElement element, string propertyName)
    {
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out JsonElement property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static void ValidateManifest(
        IReadOnlyDictionary<string, JsonElement> documents,
        List<string> errors)
    {
        if (!documents.TryGetValue(ArtifactNames.Manifest, out JsonElement manifest))
        {
            return;
        }

        var schemaVersion = ReadStringProperty(manifest, "schemaVersion");

        if (schemaVersion is not null &&
            !string.Equals(schemaVersion, ContractVersion.Current, StringComparison.Ordinal))
        {
            errors.Add(
                $"manifest.schemaVersion must be '{ContractVersion.Current}' but was '{schemaVersion}'.");
        }

        if (!manifest.TryGetProperty("artifacts", out JsonElement artifactMap) ||
            artifactMap.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach ((var propertyName, var expectedFileName) in ManifestArtifactProperties)
        {
            var actualFileName = ReadStringProperty(artifactMap, propertyName);

            if (actualFileName is not null &&
                !string.Equals(actualFileName, expectedFileName, StringComparison.Ordinal))
            {
                errors.Add(
                    $"manifest.artifacts.{propertyName} must be '{expectedFileName}' but was '{actualFileName}'.");
            }
        }
    }

    private static void ValidateJsonArtifactVersions(
        IReadOnlyDictionary<string, JsonElement> documents,
        string? manifestSchemaVersion,
        List<string> errors)
    {
        if (manifestSchemaVersion is null)
        {
            return;
        }

        foreach ((var artifactName, JsonElement document) in documents)
        {
            if (string.Equals(artifactName, ArtifactNames.Manifest, StringComparison.Ordinal))
            {
                continue;
            }

            ValidateSchemaVersion(document, artifactName, null, manifestSchemaVersion, errors);
        }
    }

    private static void ValidateSummaryCounts(
        IReadOnlyDictionary<string, JsonElement> documents,
        IReadOnlyDictionary<string, NdjsonArtifactMetadata> ndjsonMetadata,
        GraphMetadata? graphMetadata,
        List<string> errors)
    {
        if (!documents.TryGetValue(ArtifactNames.Summary, out JsonElement summary))
        {
            return;
        }

        ValidateSummaryRecordCount(
            summary,
            "metricResultCount",
            ArtifactNames.Metrics,
            ndjsonMetadata,
            errors);
        ValidateSummaryRecordCount(
            summary,
            "diagnosticCount",
            ArtifactNames.Diagnostics,
            ndjsonMetadata,
            errors);

        if (graphMetadata is null)
        {
            return;
        }

        ValidateSummaryGraphCount(summary, "fileCount", "file", graphMetadata, errors);
        ValidateSummaryGraphCount(summary, "typeCount", "type", graphMetadata, errors);
        ValidateSummaryGraphCount(summary, "memberCount", "member", graphMetadata, errors);

        ValidateSummaryGraphCount(summary, "projectCount", "project", graphMetadata, errors);
    }

    private static void ValidateSummaryRecordCount(
        JsonElement summary,
        string propertyName,
        string artifactName,
        IReadOnlyDictionary<string, NdjsonArtifactMetadata> ndjsonMetadata,
        List<string> errors)
    {
        if (!summary.TryGetProperty(propertyName, out JsonElement countElement) ||
            countElement.ValueKind != JsonValueKind.Number ||
            !countElement.TryGetInt64(out var declaredCount) ||
            !ndjsonMetadata.TryGetValue(artifactName, out NdjsonArtifactMetadata? metadata) ||
            metadata is null)
        {
            return;
        }

        if (declaredCount != metadata.RecordCount)
        {
            errors.Add(
                $"summary.{propertyName} is {declaredCount} but {artifactName} contains " +
                $"{metadata.RecordCount} record(s).");
        }
    }

    private static void ValidateSummaryGraphCount(
        JsonElement summary,
        string propertyName,
        string nodeKind,
        GraphMetadata graphMetadata,
        List<string> errors)
    {
        if (!summary.TryGetProperty(propertyName, out JsonElement countElement) ||
            countElement.ValueKind != JsonValueKind.Number ||
            !countElement.TryGetInt64(out var declaredCount))
        {
            return;
        }

        var actualCount = graphMetadata.NodeKindCounts.GetValueOrDefault(nodeKind);

        if (declaredCount != actualCount)
        {
            errors.Add(
                $"summary.{propertyName} is {declaredCount} but {ArtifactNames.Graph} contains " +
                $"{actualCount} {nodeKind} node(s).");
        }
    }

    private static void ValidateRootPathConsistency(
        IReadOnlyDictionary<string, JsonElement> documents,
        List<string> errors)
    {
        if (!documents.TryGetValue(ArtifactNames.Manifest, out JsonElement manifest) ||
            !documents.TryGetValue(ArtifactNames.Summary, out JsonElement summary))
        {
            return;
        }

        var manifestRootPath = ReadStringProperty(manifest, "rootPath");
        var summaryRootPath = ReadStringProperty(summary, "rootPath");

        if (manifestRootPath is not null &&
            summaryRootPath is not null &&
            !string.Equals(manifestRootPath, summaryRootPath, StringComparison.Ordinal))
        {
            errors.Add(
                $"summary.rootPath '{summaryRootPath}' does not match " +
                $"manifest.rootPath '{manifestRootPath}'.");
        }
    }

    private static GraphMetadata? ValidateGraph(
        IReadOnlyDictionary<string, JsonElement> documents,
        List<string> errors)
    {
        if (!documents.TryGetValue(ArtifactNames.Graph, out JsonElement graph) ||
            !graph.TryGetProperty("nodes", out JsonElement nodes) ||
            nodes.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeMetadata = new Dictionary<string, GraphNodeMetadata>(StringComparer.Ordinal);
        var nodeKindCounts = new Dictionary<string, long>(StringComparer.Ordinal);
        var nodeIndex = 0;

        foreach (JsonElement node in nodes.EnumerateArray())
        {
            var nodeId = ReadStringProperty(node, "id");
            var nodeKind = ReadStringProperty(node, "kind");
            var targetIdStability = ReadStringProperty(node, "targetIdStability");

            if (nodeId is not null && !nodeIds.Add(nodeId))
            {
                errors.Add($"graph.nodes[{nodeIndex}].id duplicates node id '{nodeId}'.");
            }

            if (nodeId is not null && nodeKind is not null && targetIdStability is not null)
            {
                nodeMetadata.TryAdd(nodeId, new GraphNodeMetadata(nodeKind, targetIdStability));
            }

            if (nodeKind is not null)
            {
                nodeKindCounts[nodeKind] = nodeKindCounts.GetValueOrDefault(nodeKind) + 1;
            }

            nodeIndex++;
        }

        if (!graph.TryGetProperty("edges", out JsonElement edges) ||
            edges.ValueKind != JsonValueKind.Array)
        {
            return new GraphMetadata(nodeMetadata, nodeKindCounts);
        }

        var edgeIndex = 0;

        foreach (JsonElement edge in edges.EnumerateArray())
        {
            ValidateEdgeEndpoint(edge, "from", edgeIndex, nodeIds, errors);
            ValidateEdgeEndpoint(edge, "to", edgeIndex, nodeIds, errors);
            edgeIndex++;
        }

        return new GraphMetadata(nodeMetadata, nodeKindCounts);
    }

    private static void ValidateTargetReferences(
        IReadOnlyDictionary<string, NdjsonArtifactMetadata> ndjsonMetadata,
        GraphMetadata? graphMetadata,
        List<string> errors)
    {
        if (graphMetadata is null)
        {
            return;
        }

        foreach (var artifactName in new[] { ArtifactNames.Metrics, ArtifactNames.Chunks })
        {
            if (!ndjsonMetadata.TryGetValue(artifactName, out NdjsonArtifactMetadata? metadata) ||
                metadata is null)
            {
                continue;
            }

            foreach (ArtifactTargetReference reference in metadata.TargetReferences)
            {
                if (!RequiresGraphNode(reference.TargetKind))
                {
                    continue;
                }

                if (!graphMetadata.NodeMetadata.TryGetValue(
                        reference.TargetId,
                        out GraphNodeMetadata? graphNode) ||
                    graphNode is null)
                {
                    errors.Add(
                        $"{artifactName}:{reference.LineNumber} targetId '{reference.TargetId}' " +
                        "references a missing graph node.");
                    continue;
                }

                if (!string.Equals(reference.TargetKind, graphNode.TargetKind, StringComparison.Ordinal))
                {
                    errors.Add(
                        $"{artifactName}:{reference.LineNumber} targetKind '{reference.TargetKind}' " +
                        $"does not match graph node kind '{graphNode.TargetKind}' for targetId " +
                        $"'{reference.TargetId}'.");
                    continue;
                }

                if (!string.Equals(
                        reference.TargetIdStability,
                        graphNode.TargetIdStability,
                        StringComparison.Ordinal))
                {
                    errors.Add(
                        $"{artifactName}:{reference.LineNumber} targetIdStability " +
                        $"'{reference.TargetIdStability}' does not match graph node targetIdStability " +
                        $"'{graphNode.TargetIdStability}' for targetId '{reference.TargetId}'.");
                }
            }
        }
    }

    private static bool RequiresGraphNode(string targetKind)
    {
        return targetKind is "solution" or "project" or "file" or "type" or "member";
    }

    private static void ValidateEdgeEndpoint(
        JsonElement edge,
        string propertyName,
        int edgeIndex,
        HashSet<string> nodeIds,
        List<string> errors)
    {
        var endpoint = ReadStringProperty(edge, propertyName);

        if (endpoint is not null && !nodeIds.Contains(endpoint))
        {
            errors.Add(
                $"graph.edges[{edgeIndex}].{propertyName} references missing node '{endpoint}'.");
        }
    }

    private sealed record GraphNodeMetadata(
        string TargetKind,
        string TargetIdStability);

    private sealed record GraphMetadata(
        IReadOnlyDictionary<string, GraphNodeMetadata> NodeMetadata,
        IReadOnlyDictionary<string, long> NodeKindCounts);
}
