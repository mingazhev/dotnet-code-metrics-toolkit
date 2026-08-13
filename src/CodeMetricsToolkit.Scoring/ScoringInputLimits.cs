using System.Runtime.CompilerServices;
using System.Text;

namespace CodeMetricsToolkit.Scoring;

internal sealed record ScoringInputLimits(
    int MaxProfileBytes,
    int MaxJsonArtifactBytes,
    long MaxMetricsBytes,
    int MaxMetricLineCharacters,
    int MaxMetricLines,
    int MaxMetricRecords,
    int MaxJsonDepth)
{
    public const int DefaultMaxProfileBytes = 1024 * 1024;
    public const int DefaultMaxJsonArtifactBytes = 128 * 1024 * 1024;
    public const long DefaultMaxMetricsBytes = 1024L * 1024 * 1024;
    public const int DefaultMaxMetricLineCharacters = 8 * 1024 * 1024;
    public const int DefaultMaxMetricLines = 1_000_000;
    public const int DefaultMaxMetricRecords = 1_000_000;
    public const int DefaultMaxJsonDepth = 64;

    public static ScoringInputLimits Default { get; } = new(
        DefaultMaxProfileBytes,
        DefaultMaxJsonArtifactBytes,
        DefaultMaxMetricsBytes,
        DefaultMaxMetricLineCharacters,
        DefaultMaxMetricLines,
        DefaultMaxMetricRecords,
        DefaultMaxJsonDepth);

    public void Validate()
    {
        if (MaxProfileBytes <= 0 ||
            MaxJsonArtifactBytes <= 0 ||
            MaxMetricsBytes <= 0 ||
            MaxMetricLineCharacters <= 0 ||
            MaxMetricLines <= 0 ||
            MaxMetricRecords <= 0 ||
            MaxJsonDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ScoringInputLimits),
                "Scoring input limits must be positive.");
        }
    }
}

internal static class ScoringInputReader
{
    public static async Task<byte[]> ReadFileBytesAsync(
        string path,
        int maxBytes,
        string displayName,
        CancellationToken cancellationToken)
    {
        var declaredLength = new FileInfo(path).Length;
        if (declaredLength > maxBytes)
        {
            throw TooLarge(displayName, declaredLength, maxBytes);
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        return await ReadStreamBytesAsync(
            stream,
            maxBytes,
            displayName,
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<byte[]> ReadStreamBytesAsync(
        Stream stream,
        int maxBytes,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (stream.CanSeek && stream.Length - stream.Position > maxBytes)
        {
            throw TooLarge(displayName, stream.Length - stream.Position, maxBytes);
        }

        await using var output = new MemoryStream();
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (read > maxBytes - output.Length)
            {
                throw new InvalidDataException(
                    $"{displayName} exceeds the maximum of {maxBytes} bytes.");
            }

            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
        }

        return output.ToArray();
    }

    public static async IAsyncEnumerable<BoundedInputLine> ReadLinesAsync(
        Stream stream,
        string displayName,
        int maxLineCharacters,
        int maxLines,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(
            stream,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: true);
        var line = new StringBuilder();
        var characters = new char[8 * 1024];
        var lineNumber = 0;
        int read;

        while ((read = await reader.ReadAsync(characters, cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (var index = 0; index < read; index++)
            {
                var character = characters[index];
                if (character == '\n')
                {
                    lineNumber++;
                    EnsureLineCount(displayName, lineNumber, maxLines);
                    if (line.Length > 0 && line[^1] == '\r')
                    {
                        line.Length--;
                    }

                    yield return new BoundedInputLine(lineNumber, line.ToString());
                    line.Clear();
                    continue;
                }

                if (line.Length >= maxLineCharacters)
                {
                    throw new InvalidDataException(
                        $"{displayName}:{lineNumber + 1} exceeds the maximum line length of " +
                        $"{maxLineCharacters} characters.");
                }

                line.Append(character);
            }
        }

        if (line.Length > 0)
        {
            lineNumber++;
            EnsureLineCount(displayName, lineNumber, maxLines);
            if (line[^1] == '\r')
            {
                line.Length--;
            }

            yield return new BoundedInputLine(lineNumber, line.ToString());
        }
    }

    private static void EnsureLineCount(string displayName, int count, int maximum)
    {
        if (count > maximum)
        {
            throw new InvalidDataException(
                $"{displayName} exceeds the maximum line count of {maximum}.");
        }
    }

    private static InvalidDataException TooLarge(
        string displayName,
        long actualBytes,
        long maximumBytes)
    {
        return new InvalidDataException(
            $"{displayName} is {actualBytes} bytes and exceeds the maximum of {maximumBytes} bytes.");
    }
}

internal sealed record BoundedInputLine(int Number, string Text);

internal sealed class ScoringByteLimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string _displayName;
    private long _bytesRead;

    public ScoringByteLimitedReadStream(Stream inner, long maxBytes, string displayName)
    {
        _inner = inner;
        _maxBytes = maxBytes;
        _displayName = displayName;
    }

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
        var read = _inner.Read(buffer, offset, count);
        Track(read);
        return read;
    }

    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        Track(read);
        return read;
    }

    public override async ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Track(read);
        return read;
    }

    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private void Track(int read)
    {
        _bytesRead += read;
        if (_bytesRead > _maxBytes)
        {
            throw new InvalidDataException(
                $"{_displayName} exceeds the maximum of {_maxBytes} bytes.");
        }
    }
}
