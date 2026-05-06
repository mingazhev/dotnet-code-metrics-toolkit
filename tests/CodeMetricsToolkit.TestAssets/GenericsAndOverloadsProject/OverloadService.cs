namespace GenericsAndOverloadsProject;

public sealed class OverloadService
{
    public string Name { get; }

    public OverloadService()
        : this("default")
    {
    }

    public OverloadService(string name)
    {
        Name = name;
    }

    public string Format(int value)
    {
        return $"{Name}:{value.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
    }

    public string Format(string value)
    {
        return $"{Name}:{value.Trim()}";
    }

    public string Format<T>(T value)
    {
        return $"{Name}:{value?.ToString() ?? string.Empty}";
    }

    public string Format<T>(T value, IFormatProvider provider)
        where T : IFormattable
    {
        return $"{Name}:{value.ToString(null, provider)}";
    }
}
