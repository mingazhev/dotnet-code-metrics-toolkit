using System.Text;

namespace CodeMetricsToolkit.Core.Syntax;

internal sealed class BoundedProcessOutput
{
    private const int MaximumCharacters = 16 * 1024;
    private readonly StringBuilder _buffer = new(MaximumCharacters);

    public bool WasTruncated { get; private set; }

    public void Append(string? line)
    {
        if (line is null || WasTruncated)
        {
            return;
        }

        var remaining = MaximumCharacters - _buffer.Length;
        if (remaining <= 0)
        {
            WasTruncated = true;
            return;
        }

        if (_buffer.Length > 0)
        {
            _buffer.Append('\n');
            remaining--;
        }

        if (line.Length <= remaining)
        {
            _buffer.Append(line);
            return;
        }

        _buffer.Append(line.AsSpan(0, Math.Max(0, remaining)));
        WasTruncated = true;
    }

    public void AppendChunk(ReadOnlySpan<char> chunk)
    {
        if (chunk.IsEmpty || WasTruncated)
        {
            return;
        }

        var retainedLength = Math.Min(chunk.Length, MaximumCharacters - _buffer.Length);
        _buffer.Append(chunk[..retainedLength]);
        WasTruncated = retainedLength < chunk.Length;
    }

    public override string ToString()
    {
        return WasTruncated
            ? _buffer.ToString() + "\n[process output truncated]"
            : _buffer.ToString();
    }
}
