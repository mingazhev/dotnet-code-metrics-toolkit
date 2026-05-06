namespace BrokenProject;

public sealed class BrokenSyntax
{
    public int MissingSemicolon()
    {
        return 42
    }

    public void MissingBrace()
    {
        if (DateTime.UtcNow.Year > 2000)
        {
            Console.WriteLine("still parseable enough for diagnostics");
    }
}
