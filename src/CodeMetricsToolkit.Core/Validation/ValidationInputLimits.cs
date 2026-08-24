using System.Text;

namespace CodeMetricsToolkit.Core.Validation;

internal sealed record ValidationInputLimits(
    long MaxJsonArtifactBytes,
    long MaxNdjsonArtifactBytes,
    int MaxNdjsonLineCharacters,
    int MaxNdjsonLines,
    int MaxNdjsonRecords,
    int MaxJsonDepth)
{
    public const long DefaultMaxJsonArtifactBytes = 128L * 1024 * 1024;
    public const long DefaultMaxNdjsonArtifactBytes = 1024L * 1024 * 1024;
    public const int DefaultMaxNdjsonLineCharacters = 8 * 1024 * 1024;
    public const int DefaultMaxNdjsonLines = 1_000_000;
    public const int DefaultMaxNdjsonRecords = 1_000_000;
    public const int DefaultMaxJsonDepth = 64;

    public static ValidationInputLimits Default { get; } = new(
        DefaultMaxJsonArtifactBytes,
        DefaultMaxNdjsonArtifactBytes,
        DefaultMaxNdjsonLineCharacters,
        DefaultMaxNdjsonLines,
        DefaultMaxNdjsonRecords,
        DefaultMaxJsonDepth);

    public void Validate()
    {
        if (MaxJsonArtifactBytes <= 0 ||
            MaxJsonArtifactBytes > int.MaxValue ||
            MaxNdjsonArtifactBytes <= 0 ||
            MaxNdjsonLineCharacters <= 0 ||
            MaxNdjsonLines <= 0 ||
            MaxNdjsonRecords <= 0 ||
            MaxJsonDepth <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ValidationInputLimits),
                "Validation input limits must be positive and JSON byte limits must fit in an array.");
        }
    }
}

internal static class ValidationInputReader
{
    public static byte[] ReadJsonBytes(string path, long maxBytes)
    {
        var artifactName = Path.GetFileName(path);
        var declaredLength = new FileInfo(path).Length;
        if (declaredLength > maxBytes)
        {
            throw TooLarge(artifactName, declaredLength, maxBytes);
        }

        var bytes = new byte[(int)declaredLength];
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read == 0)
            {
                Array.Resize(ref bytes, offset);
                return bytes;
            }

            offset += read;
        }

        if (stream.ReadByte() != -1)
        {
            throw new InvalidDataException(
                $"{artifactName} changed while it was read; retry with a stable artifact generation.");
        }

        return bytes;
    }

    public static IEnumerable<BoundedTextLine> ReadNdjsonLines(
        string path,
        ValidationInputLimits limits,
        CancellationToken cancellationToken)
    {
        var artifactName = Path.GetFileName(path);
        var declaredLength = new FileInfo(path).Length;
        if (declaredLength > limits.MaxNdjsonArtifactBytes)
        {
            throw TooLarge(artifactName, declaredLength, limits.MaxNdjsonArtifactBytes);
        }

        using var file = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);
        using var bounded = new ByteLimitedReadStream(
            file,
            limits.MaxNdjsonArtifactBytes,
            artifactName);
        using var reader = new StreamReader(
            bounded,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 64 * 1024,
            leaveOpen: false);
        var line = new StringBuilder();
        var characters = new char[8 * 1024];
        var lineNumber = 0;
        int read;

        while ((read = reader.Read(characters, 0, characters.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var index = 0; index < read; index++)
            {
                var character = characters[index];
                if (character == '\n')
                {
                    lineNumber++;
                    EnsureLineCount(artifactName, lineNumber, limits.MaxNdjsonLines);
                    if (line.Length > 0 && line[^1] == '\r')
                    {
                        line.Length--;
                    }

                    yield return new BoundedTextLine(lineNumber, line.ToString());
                    line.Clear();
                    continue;
                }

                if (line.Length >= limits.MaxNdjsonLineCharacters)
                {
                    throw new InvalidDataException(
                        $"{artifactName}:{lineNumber + 1} exceeds the maximum line length of " +
                        $"{limits.MaxNdjsonLineCharacters} characters.");
                }

                line.Append(character);
            }
        }

        if (line.Length > 0)
        {
            lineNumber++;
            EnsureLineCount(artifactName, lineNumber, limits.MaxNdjsonLines);
            if (line[^1] == '\r')
            {
                line.Length--;
            }

            yield return new BoundedTextLine(lineNumber, line.ToString());
        }
    }

    private static void EnsureLineCount(string artifactName, int lineCount, int maximum)
    {
        if (lineCount > maximum)
        {
            throw new InvalidDataException(
                $"{artifactName} exceeds the maximum line count of {maximum}.");
        }
    }

    private static InvalidDataException TooLarge(
        string artifactName,
        long actualBytes,
        long maximumBytes)
    {
        return new InvalidDataException(
            $"{artifactName} is {actualBytes} bytes and exceeds the maximum of {maximumBytes} bytes.");
    }
}

internal sealed record BoundedTextLine(int Number, string Text);

internal sealed class ByteLimitedReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _maxBytes;
    private readonly string _displayName;
    private long _bytesRead;

    public ByteLimitedReadStream(Stream inner, long maxBytes, string displayName)
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
