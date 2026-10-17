using System.Text.Json;

namespace CodeMetricsToolkit.Tests.Cli;

public sealed partial class CliAnalyzeTests
{
    [Fact]
    public async Task AnalyzeCommandEmitsProjectAndNamespaceDependenciesIncludingDirectTypeUsageSites()
    {
        using var input = TemporaryDirectory.Create();
        using var output = TemporaryDirectory.Create();
        var projectA = Path.Combine(input.Path, "ProjectA");
        var projectB = Path.Combine(input.Path, "ProjectB");
        Directory.CreateDirectory(projectA);
        Directory.CreateDirectory(projectB);
        File.WriteAllText(
            Path.Combine(projectA, "ProjectA.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../ProjectB/ProjectB.csproj" /></ItemGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(projectB, "ProjectB.csproj"),
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
            </Project>
            """);
        File.WriteAllText(
            Path.Combine(projectA, "A.cs"),
            """
            using System.Collections.Generic;
            using Other;
            using Shared;

            namespace Shared
            {
                [RemoteAttribute]
                public sealed class AttributeUse { }
                public sealed class FieldUse { public FieldType Field; }
                public sealed class SignatureUse { public SignatureType Convert(SignatureType value) => value; }
                public sealed class GenericUse { public List<GenericType> Values; }
                public sealed class StaticFieldUse { public object Read() => StaticStore.Field; }
                public sealed class StaticPropertyUse { public object Read() => StaticStore.Property; }
                public sealed class StaticCallUse { public object Read() => StaticStore.Call(); }
                public sealed class CrossNamespaceUse { public OtherType Value; }
                public sealed class NestedContainer { public sealed class Nested { public GlobalRemote OnlyNestedUse; } }
            }

            public sealed class LocalGlobal { }
            """);
        File.WriteAllText(
            Path.Combine(projectB, "B.cs"),
            """
            using System;
            namespace Shared
            {
                [AttributeUsage(AttributeTargets.Class)]
                public sealed class RemoteAttribute : Attribute { }
                public sealed class FieldType { }
                public sealed class SignatureType { }
                public sealed class GenericType { }
                public static class StaticStore
                {
                    public static FieldType Field = new();
                    public static FieldType Property => new();
                    public static FieldType Call() => new();
                }
            }
            namespace Other { public sealed class OtherType { public OtherClient.ClientType Client; } }
            namespace OtherClient { public sealed class ClientType { public Other.OtherType Other; } }
            public sealed class GlobalRemote { }
            """);

        var exitCode = await RunCliAsync("analyze", input.Path, "--output", output.Path, "--no-restore");

        Assert.Equal(0, exitCode);
        JsonElement[] nodes = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "nodes");
        JsonElement[] edges = ReadJsonArray(Path.Combine(output.Path, "graph.json"), "edges");
        JsonElement[] metrics = ReadNdjson(Path.Combine(output.Path, "metrics.ndjson"));
        JsonElement projectANode = Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "project") && HasPropertyValue(node, "name", "ProjectA"));
        JsonElement projectBNode = Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "project") && HasPropertyValue(node, "name", "ProjectB"));
        var projectAId = projectANode.GetProperty("id").GetString()!;
        var projectBId = projectBNode.GetProperty("id").GetString()!;

        Assert.Contains(edges, edge =>
            HasPropertyValue(edge, "from", projectAId) &&
            HasPropertyValue(edge, "to", projectBId) &&
            HasPropertyValue(edge, "kind", "depends_on"));
        AssertMetric(metrics, projectAId, "outgoing_project_dependency_count", 1);
        AssertMetric(metrics, projectBId, "incoming_project_dependency_count", 1);
        AssertMetric(metrics, projectAId, "project_dependency_cycle_membership", 0);
        AssertMetric(metrics, projectAId, "project_dependency_component_size", 0);

        JsonElement[] sharedNamespaces = nodes.Where(node =>
            HasPropertyValue(node, "kind", "namespace") && HasPropertyValue(node, "name", "Shared")).ToArray();
        Assert.Equal(2, sharedNamespaces.Length);
        Assert.Equal(2, sharedNamespaces.Select(node => node.GetProperty("id").GetString()).Distinct().Count());
        Assert.Contains(nodes, node =>
            HasPropertyValue(node, "kind", "namespace") && HasPropertyValue(node, "name", "(global)"));

        JsonElement fieldType = FindTypeNode(nodes, "Shared.FieldType");
        JsonElement signatureType = FindTypeNode(nodes, "Shared.SignatureType");
        JsonElement genericType = FindTypeNode(nodes, "Shared.GenericType");
        JsonElement attributeType = FindTypeNode(nodes, "Shared.RemoteAttribute");
        JsonElement staticStore = FindTypeNode(nodes, "Shared.StaticStore");
        JsonElement otherType = FindTypeNode(nodes, "Other.OtherType");
        JsonElement otherClientType = FindTypeNode(nodes, "OtherClient.ClientType");
        JsonElement globalRemote = Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "type") && HasPropertyValue(node, "name", "GlobalRemote"));
        var nestedContainerId = FindTypeNode(nodes, "Shared.NestedContainer").GetProperty("id").GetString()!;
        var fieldUseId = FindTypeNode(nodes, "Shared.FieldUse").GetProperty("id").GetString()!;
        var signatureUseId = FindTypeNode(nodes, "Shared.SignatureUse").GetProperty("id").GetString()!;
        var genericUseId = FindTypeNode(nodes, "Shared.GenericUse").GetProperty("id").GetString()!;
        var attributeUseId = FindTypeNode(nodes, "Shared.AttributeUse").GetProperty("id").GetString()!;
        var staticFieldUseId = FindTypeNode(nodes, "Shared.StaticFieldUse").GetProperty("id").GetString()!;
        var staticPropertyUseId = FindTypeNode(nodes, "Shared.StaticPropertyUse").GetProperty("id").GetString()!;
        var staticCallUseId = FindTypeNode(nodes, "Shared.StaticCallUse").GetProperty("id").GetString()!;
        var crossNamespaceUseId = FindTypeNode(nodes, "Shared.CrossNamespaceUse").GetProperty("id").GetString()!;
        var nestedId = FindTypeNode(nodes, "Shared.NestedContainer.Nested").GetProperty("id").GetString()!;
        var fieldTypeId = fieldType.GetProperty("id").GetString()!;
        var signatureTypeId = signatureType.GetProperty("id").GetString()!;
        var genericTypeId = genericType.GetProperty("id").GetString()!;
        var attributeTypeId = attributeType.GetProperty("id").GetString()!;
        var staticStoreId = staticStore.GetProperty("id").GetString()!;
        var otherTypeId = otherType.GetProperty("id").GetString()!;
        var otherClientTypeId = otherClientType.GetProperty("id").GetString()!;
        var globalRemoteId = globalRemote.GetProperty("id").GetString()!;

        AssertTypeUseEdge(edges, fieldUseId, fieldTypeId);
        AssertTypeUseEdge(edges, signatureUseId, signatureTypeId);
        AssertTypeUseEdge(edges, genericUseId, genericTypeId);
        AssertTypeUseEdge(edges, attributeUseId, attributeTypeId);
        AssertTypeUseEdge(edges, staticFieldUseId, staticStoreId);
        AssertTypeUseEdge(edges, staticPropertyUseId, staticStoreId);
        AssertTypeUseEdge(edges, staticCallUseId, staticStoreId);
        AssertTypeUseEdge(edges, crossNamespaceUseId, otherTypeId);
        AssertTypeUseEdge(edges, otherTypeId, otherClientTypeId);
        AssertTypeUseEdge(edges, otherClientTypeId, otherTypeId);
        Assert.Contains(edges, edge =>
            HasPropertyValue(edge, "from", nestedId) &&
            HasPropertyValue(edge, "to", globalRemoteId) &&
            HasPropertyValue(edge, "kind", "uses_type"));
        Assert.DoesNotContain(edges, edge =>
            HasPropertyValue(edge, "from", nestedContainerId) &&
            HasPropertyValue(edge, "to", globalRemoteId) &&
            HasPropertyValue(edge, "kind", "uses_type"));
        Assert.DoesNotContain(edges, edge => HasPropertyValue(edge, "kind", "uses_type") &&
            nodes.Any(node => HasPropertyValue(node, "id", edge.GetProperty("to").GetString()!) &&
                node.GetProperty("name").GetString() == "System.String"));

        JsonElement sharedNamespaceA = Assert.Single(sharedNamespaces, node =>
            HasPropertyValue(node, "filePath", "ProjectA/ProjectA.csproj"));
        JsonElement otherNamespaceB = Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "namespace") && HasPropertyValue(node, "name", "Other"));
        JsonElement otherClientNamespaceB = Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "namespace") && HasPropertyValue(node, "name", "OtherClient"));
        Assert.Contains(edges, edge =>
            HasPropertyValue(edge, "from", sharedNamespaceA.GetProperty("id").GetString()!) &&
            HasPropertyValue(edge, "to", otherNamespaceB.GetProperty("id").GetString()!) &&
            HasPropertyValue(edge, "kind", "depends_on"));
        AssertMetric(metrics, sharedNamespaceA.GetProperty("id").GetString()!, "outgoing_namespace_dependency_count", 3);
        AssertMetric(metrics, otherNamespaceB.GetProperty("id").GetString()!, "incoming_namespace_dependency_count", 2);
        AssertMetric(metrics, otherNamespaceB.GetProperty("id").GetString()!, "namespace_dependency_cycle_membership", 1);
        AssertMetric(metrics, otherNamespaceB.GetProperty("id").GetString()!, "namespace_dependency_component_size", 2);
        AssertMetric(metrics, otherClientNamespaceB.GetProperty("id").GetString()!, "namespace_dependency_component_size", 2);
        Assert.Contains(edges, edge => HasPropertyValue(edge, "kind", "contains") &&
            sharedNamespaces.Any(ns => HasPropertyValue(ns, "id", edge.GetProperty("from").GetString()!) &&
                HasPropertyValue(edge, "to", FindTypeNode(nodes, "Shared.FieldUse").GetProperty("id").GetString()!)));
    }

    private static JsonElement FindTypeNode(JsonElement[] nodes, string name)
    {
        return Assert.Single(nodes, node =>
            HasPropertyValue(node, "kind", "type") && HasPropertyValue(node, "name", name));
    }

    private static void AssertTypeUseEdge(JsonElement[] edges, string sourceTypeId, string targetTypeId)
    {
        Assert.Contains(edges, edge =>
            HasPropertyValue(edge, "from", sourceTypeId) &&
            HasPropertyValue(edge, "to", targetTypeId) &&
            HasPropertyValue(edge, "kind", "uses_type"));
    }

    private static void AssertMetric(JsonElement[] metrics, string targetId, string metricId, int expected)
    {
        JsonElement metric = Assert.Single(metrics, candidate =>
            HasPropertyValue(candidate, "targetId", targetId) && HasPropertyValue(candidate, "metricId", metricId));
        Assert.Equal(expected, metric.GetProperty("numericValue").GetInt32());
    }
}
