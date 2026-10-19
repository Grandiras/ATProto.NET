using System.Buffers;

namespace ATProtoNet.Data;

// Reads varint-length-prefixed frames from a stream, as CAR and STAR are framed: bounded buffering,
// every byte counted against a total limit before it is buffered past it. Without read-ahead it reads
// exactly what it is asked for, so a caller can read a header and leave the stream at the first frame.
internal sealed class FrameReader(Stream stream, long maxTotalBytes, string totalLimit, bool readAhead) : IDisposable
{
    private const int BufferSize = 64 * 1024;

    private byte[] _buffer = ArrayPool<byte>.Shared.Rent(readAhead ? BufferSize : UnsignedVarint.MaxLength);
    private int _start, _end;
    private long _total;

    // The next varint, or null at the end of the stream when allowEnd and nothing of it was read.
    public async ValueTask<ulong?> ReadVarintAsync(bool allowEnd, CancellationToken cancellationToken)
    {
        while (true)
        {
            switch (UnsignedVarint.TryRead(_buffer.AsSpan(_start, _end - _start), out var value, out var length))
            {
                case OperationStatus.Done:
                    _start += length;
                    return value;
                case OperationStatus.InvalidData:
                    throw new FormatException($"A length prefix is longer than {UnsignedVarint.MaxLength} bytes or not minimally encoded.");
            }
            if (await FillAsync(cancellationToken).ConfigureAwait(false)) continue;
            if (allowEnd && _start == _end) return null;
            throw new FormatException("The input ends inside a length prefix.");
        }
    }

    // Exactly length bytes, in a new array: what is buffered, then the rest read straight into it.
    public async ValueTask<byte[]> ReadExactlyAsync(int length, CancellationToken cancellationToken)
    {
        var frame = new byte[length];
        var buffered = Math.Min(length, _end - _start);
        _buffer.AsSpan(_start, buffered).CopyTo(frame);
        _start += buffered;
        if (buffered == length) return frame;
        Count(length - buffered);
        try
        {
            await stream.ReadExactlyAsync(frame.AsMemory(buffered), cancellationToken).ConfigureAwait(false);
        }
        catch (EndOfStreamException e)
        {
            throw new FormatException("The input ends inside a frame.", e);
        }
        return frame;
    }

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private async ValueTask<bool> FillAsync(CancellationToken cancellationToken)
    {
        if (_start > 0)
        {
            _buffer.AsSpan(_start, _end - _start).CopyTo(_buffer);
            (_start, _end) = (0, _end - _start);
        }
        var read = await stream.ReadAsync(_buffer.AsMemory(_end, readAhead ? _buffer.Length - _end : 1), cancellationToken).ConfigureAwait(false);
        Count(read);
        _end += read;
        return read > 0;
    }

    private void Count(long bytes)
    {
        _total += bytes;
        if (_total > maxTotalBytes) throw new DataLimitExceededException(totalLimit, $"The input is longer than {totalLimit} ({maxTotalBytes} bytes).");
    }
}
