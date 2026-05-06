namespace BrokenProject;

public sealed class MissingReference
{
    public MissingDependency Create()
    {
        return new MissingDependency();
    }
}
