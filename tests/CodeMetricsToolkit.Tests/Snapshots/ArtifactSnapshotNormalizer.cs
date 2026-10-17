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

}
