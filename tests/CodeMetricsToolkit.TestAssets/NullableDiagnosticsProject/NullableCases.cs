namespace NullableDiagnosticsProject;

public sealed class NullableCases
{
    private readonly string _prefix;

    public NullableCases()
    {
        _prefix = "nullable";
    }

    public string ReturnsMaybeNull(bool useNull)
    {
        string? candidate = useNull ? null : _prefix;
        return candidate;
    }

    public int DereferenceMaybeNull(string? text)
    {
        return text.Length + _prefix.Length;
    }

    public void AssignNullToNonNullable()
    {
        string value = null;
        Console.WriteLine(value.Length + _prefix.Length);
    }
}
