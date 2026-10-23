namespace RecursiveConstraints;

public sealed class Sample<T> where T : System.IComparable<T>
{
    public T Echo(T value) => value;
}
