using System.Reflection;
using System.Reflection.Emit;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Scoring;

namespace CodeMetricsToolkit.Tests.Architecture;

public sealed class DependencyBoundaryTests
{
    [Theory]
    [InlineData("CodeMetricsToolkit.Core.Reporting", "CodeMetricsToolkit.Core.Analysis")]
    [InlineData("CodeMetricsToolkit.Core.Validation", "CodeMetricsToolkit.Core.Discovery")]
    public void SharedModelsAndFileChecksDoNotRequireHigherLayers(string sourceNamespace, string forbiddenNamespace)
    {
        Type[] types = typeof(FileFacts).Assembly.GetTypes()
            .Where(type => type.Namespace == sourceNamespace)
            .ToArray();

        Assert.NotEmpty(types);
        Assert.All(types, type => Assert.DoesNotContain(ReferencedTypes(type), dependency =>
            dependency.Namespace == forbiddenNamespace));
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.FieldType == typeof(OpCode))
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [Fact]
    public void ScoringDoesNotReferenceAnalyzerOrRoslyn()
    {
        Assert.DoesNotContain(typeof(ScoringEngine).Assembly.GetReferencedAssemblies(), reference =>
            reference.Name!.StartsWith("CodeMetricsToolkit.Core", StringComparison.Ordinal) ||
            reference.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal));
    }

    [Fact]
    public void FactsDoNotDependOnAnalysisOrArtifactPublication()
    {
        Type[] facts = typeof(FileFacts).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(FileFacts).Namespace)
            .ToArray();

        Assert.NotEmpty(facts);
        Assert.All(facts, type => Assert.DoesNotContain(ReferencedTypes(type), dependency =>
            dependency.Namespace is "CodeMetricsToolkit.Core.Analysis" or
                "CodeMetricsToolkit.Core.Reporting" or "CodeMetricsToolkit.Core.Syntax" or
                "CodeMetricsToolkit.Cli"));
    }

    [Fact]
    public void MetricCalculationsDoNotAccessFilesOrDirectories()
    {
        Type[] metrics = typeof(MetricCatalog).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(MetricCatalog).Namespace)
            .ToArray();

        Assert.NotEmpty(metrics);
        Assert.All(metrics, type => Assert.DoesNotContain(ReferencedTypes(type), IsFileSystemAccess));
    }

    [Theory]
    [InlineData("CodeMetricsToolkit.Core.Reporting")]
    [InlineData("CodeMetricsToolkit.Core.Discovery")]
    public void MetricCalculationsDoNotDependOnPublicationOrDiscovery(string forbiddenNamespace)
    {
        Type[] metrics = typeof(MetricCatalog).Assembly.GetTypes()
            .Where(type => type.Namespace == typeof(MetricCatalog).Namespace)
            .ToArray();

        Assert.NotEmpty(metrics);
        Assert.All(metrics, type => Assert.DoesNotContain(ReferencedTypes(type), dependency =>
            dependency.Namespace == forbiddenNamespace));
    }

    [Fact]
    public void DependencyInspectionFindsCallsInMethodBodies()
    {
        Assert.Contains(ReferencedTypes(typeof(FileAccessExample)), IsFileSystemAccess);
    }

    [Fact]
    public void DependencyInspectionFindsTypesInMethodSignatures()
    {
        Type[] dependencies = ReferencedTypes(typeof(SignatureOnlyExample)).ToArray();

        Assert.Contains(typeof(Core.Reporting.GraphArtifact), dependencies);
        Assert.Contains(typeof(Core.Discovery.DiscoveredSources), dependencies);
    }

    private static bool IsFileSystemAccess(Type type)
    {
        return type == typeof(File) || type == typeof(Directory) ||
            type == typeof(FileInfo) || type == typeof(DirectoryInfo) || type == typeof(FileStream);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        return DirectDependencies(type).SelectMany(ExpandType);
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;
        if (type.GetElementType() is { } elementType)
        {
            foreach (Type dependency in ExpandType(elementType))
            {
                yield return dependency;
            }
        }

        foreach (Type argument in type.GetGenericArguments().SelectMany(ExpandType))
        {
            yield return argument;
        }
    }

    private static IEnumerable<Type> SignatureTypes(MethodBase method)
    {
        if (method is MethodInfo info)
        {
            yield return info.ReturnType;
        }

        foreach (ParameterInfo parameter in method.GetParameters())
        {
            yield return parameter.ParameterType;
        }

        if (method.IsGenericMethod)
        {
            foreach (Type argument in method.GetGenericArguments())
            {
                yield return argument;
            }
        }
    }

    private static IEnumerable<Type> DirectDependencies(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (type.BaseType is { } baseType)
        {
            yield return baseType;
        }

        foreach (Type implementedInterface in type.GetInterfaces())
        {
            yield return implementedInterface;
        }

        foreach (FieldInfo field in type.GetFields(flags))
        {
            yield return field.FieldType;
        }

        foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
        {
            foreach (Type dependency in SignatureTypes(method))
            {
                yield return dependency;
            }

            var il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
            for (var offset = 0; offset < il.Length;)
            {
                short value = il[offset++];
                if (value == 0xfe)
                {
                    value = unchecked((short)(0xfe00 | il[offset++]));
                }

                OpCode code = OpCodesByValue[value];
                if (code.OperandType is OperandType.InlineMethod or OperandType.InlineField or
                    OperandType.InlineType or OperandType.InlineTok)
                {
                    MemberInfo? member = method.Module.ResolveMember(
                        BitConverter.ToInt32(il, offset),
                        type.GetGenericArguments(),
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                    Type? dependency = member as Type ?? member?.DeclaringType;
                    if (dependency is not null)
                    {
                        yield return dependency;
                    }

                    if (member is MethodBase referencedMethod)
                    {
                        foreach (Type signatureType in SignatureTypes(referencedMethod))
                        {
                            yield return signatureType;
                        }
                    }
                    else if (member is FieldInfo referencedField)
                    {
                        yield return referencedField.FieldType;
                    }
                }

                offset += code.OperandType switch
                {
                    OperandType.InlineNone => 0,
                    OperandType.ShortInlineI or OperandType.ShortInlineBrTarget or OperandType.ShortInlineVar => 1,
                    OperandType.InlineVar => 2,
                    OperandType.InlineI8 or OperandType.InlineR => 8,
                    OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, offset)),
                    _ => 4
                };
            }
        }
    }

    private static class FileAccessExample
    {
        public static string Read(string path) => File.ReadAllText(path);
    }

    private static class SignatureOnlyExample
    {
        public static IReadOnlyList<Core.Reporting.GraphArtifact[]>? Project(
            Core.Discovery.DiscoveredSources sources) => null;
    }
}
