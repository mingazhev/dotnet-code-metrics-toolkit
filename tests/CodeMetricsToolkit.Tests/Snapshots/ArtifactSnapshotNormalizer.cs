using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodeMetricsToolkit.Tests.Snapshots;

internal static class ArtifactSnapshotNormalizer
{
    public static string NormalizeJsonFile(string artifactPath)
    {
        JsonNode node = JsonNode.Parse(File.ReadAllText(artifactPath)) ??
            throw new InvalidOperationException($"Could not parse JSON artifact: {artifactPath}");

        NormalizeNode(node);

        return node.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    private static void NormalizeNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                NormalizeObject(jsonObject);
                break;
            case JsonArray jsonArray:
                foreach (JsonNode? child in jsonArray)
                {
                    if (child is not null)
                    {
                        NormalizeNode(child);
                    }
                }

                SortKnownArrays(jsonArray);
                break;
        }
    }

    private static void NormalizeObject(JsonObject jsonObject)
    {
        foreach (var propertyName in jsonObject.Select(property => property.Key).ToArray())
        {
            JsonNode? value = jsonObject[propertyName];

            if (value is null)
            {
                continue;
            }

            if (propertyName is "rootPath")
            {
                jsonObject[propertyName] = "<root>";
                continue;
            }

            if (propertyName is "startedAt" or "completedAt")
            {
                jsonObject[propertyName] = "<timestamp>";
                continue;
            }

            if (propertyName is "durationMs")
            {
                jsonObject[propertyName] = 0;
                continue;
            }

            NormalizeNode(value);
        }
    }

    private static void SortKnownArrays(JsonArray jsonArray)
    {
        JsonNode? first = jsonArray.FirstOrDefault();

        if (first is not JsonObject firstObject)
        {
            return;
        }

        var sortProperty = firstObject.ContainsKey("id")
            ? "id"
            : firstObject.ContainsKey("targetId")
                ? "targetId"
                : null;

        if (sortProperty is null)
        {
            return;
        }

        JsonNode?[] sorted = jsonArray
            .OrderBy(node => node?[sortProperty]?.GetValue<string>(), StringComparer.Ordinal)
            .Select(node => node?.DeepClone())
            .ToArray();

        jsonArray.Clear();

        foreach (JsonNode? node in sorted)
        {
            jsonArray.Add(node);
        }
    }
}
