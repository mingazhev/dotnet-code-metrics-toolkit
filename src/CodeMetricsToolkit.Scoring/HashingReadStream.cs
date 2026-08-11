using System.Security.Cryptography;

namespace CodeMetricsToolkit.Scoring;

/// <summary>
/// Appends every byte read from a single input stream to a caller-owned incremental hash. This
/// keeps parsing and provenance tied to the same immutable read snapshot without buffering the
/// complete metrics artifact in memory.
/// </summary>
internal sealed class HashingReadStream : Stream
{
    private readonly Stream _inner;
    private readonly IncrementalHash _hash;

    public HashingReadStream(Stream inner, IncrementalHash hash)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(hash);

        if (!inner.CanRead)
        {
            throw new ArgumentException("The wrapped stream must be readable.", nameof(inner));
        }

        _inner = inner;
        _hash = hash;
    }

    public bool ReachedEnd { get; private set; }

    public override bool CanRead => _inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _inner.Read(buffer, offset, count);
        Track(buffer.AsSpan(offset, bytesRead), bytesRead);

        return bytesRead;
    }

    public override int Read(Span<byte> buffer)
    {
        var bytesRead = _inner.Read(buffer);
        Track(buffer[..bytesRead], bytesRead);

        return bytesRead;
    }

    public override async Task<int> ReadAsync(
        byte[] buffer,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        var bytesRead = await _inner.ReadAsync(
            buffer.AsMemory(offset, count),
            cancellationToken).ConfigureAwait(false);
        Track(buffer.AsSpan(offset, bytesRead), bytesRead);

        return bytesRead;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var bytesRead = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Track(buffer.Span[..bytesRead], bytesRead);

        return bytesRead;
    }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    private void Track(ReadOnlySpan<byte> bytes, int bytesRead)
    {
        if (bytesRead == 0)
        {
            ReachedEnd = true;

            return;
        }

        _hash.AppendData(bytes);
    }
}
