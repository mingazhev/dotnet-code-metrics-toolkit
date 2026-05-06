namespace CodeMetricsToolkit.Core.Syntax;

public static class TargetIds
{
    public static string File(string relativePath)
    {
        return $"file:{relativePath}";
    }

    public static string TypeSemantic(string assemblyName, string documentationCommentId)
    {
        return $"type:{assemblyName}/{documentationCommentId}";
    }

    public static string MemberSemantic(string assemblyName, string documentationCommentId)
    {
        return $"member:{assemblyName}/{documentationCommentId}";
    }

    public static string Type(string projectKey, string typeName, string relativePath)
    {
        return $"type:{projectKey}/{typeName}@{relativePath}";
    }

    public static string Member(
        string projectKey,
        string containingTypeName,
        string memberName,
        int parameterCount,
        string relativePath,
        int startLine)
    {
        return $"member:{projectKey}/{containingTypeName}.{memberName}#{parameterCount}@{relativePath}:{startLine}";
    }
}
