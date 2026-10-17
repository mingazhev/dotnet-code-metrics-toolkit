using System.Reflection;
using System.Reflection.Emit;
using CodeMetricsToolkit.Core.Facts;
using CodeMetricsToolkit.Core.Metrics;
using CodeMetricsToolkit.Scoring;

namespace CodeMetricsToolkit.Tests.Architecture;

public sealed class DependencyBoundaryTests
{
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

    [Fact]
    public void DependencyInspectionFindsCallsInMethodBodies()
    {
        Assert.Contains(ReferencedTypes(typeof(FileAccessExample)), IsFileSystemAccess);
    }

    private static bool IsFileSystemAccess(Type type)
    {
        return type == typeof(File) || type == typeof(Directory) ||
            type == typeof(FileInfo) || type == typeof(DirectoryInfo) || type == typeof(FileStream);
    }

    private static IEnumerable<Type> ReferencedTypes(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (FieldInfo field in type.GetFields(flags))
        {
            yield return field.FieldType;
            foreach (Type argument in field.FieldType.GetGenericArguments())
            {
                yield return argument;
            }
        }

        foreach (MethodBase method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags)))
        {
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
}
