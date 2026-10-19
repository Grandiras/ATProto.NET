using System.Buffers;
using System.Text.Unicode;

namespace ATProtoNet.Data;

/// <summary>A forward-only writer of canonical DRISL-CBOR.</summary>
/// <remarks>
/// The output is canonical whatever order map entries are written in: definite lengths, shortest
/// encodings, keys sorted shorter first and then by their UTF-8 bytes. A top-level value reaches the
/// output once it is complete. There is no way to write a float. See
/// <see href="https://dasl.ing/drisl.html"/>.
/// </remarks>
public sealed class DrislWriter : IDataModelSink
{
    private readonly IBufferWriter<byte>? _output;
    private byte[] _buffer = [];
    private int _length, _depth, _entryCount;
    private Frame[] _frames = new Frame[4];
    private int[] _entries = new int[8];

    /// <summary>Creates a writer that sends each complete top-level value to <paramref name="output"/>.</summary>
    /// <param name="output">Where the encoded values go.</param>
    public DrislWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    // Keeps the encoded value for ToArray.
    internal DrislWriter() { }

    /// <summary>Writes the start of a map of <paramref name="count"/> entries.</summary>
    /// <param name="count">The number of entries that follow, checked by <see cref="WriteEndMap"/>.</param>
    public void WriteStartMap(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Start(isMap: true, count);
    }

    /// <summary>Writes a map key; the value follows.</summary>
    /// <param name="key">The key.</param>
    /// <exception cref="ArgumentException">The key repeats an earlier key of the map, or is not valid UTF-16.</exception>
    public void WriteKey(string key)
    {
        ref var frame = ref KeyFrame();
        var start = _length;
        WriteText(key);
        AddKey(ref frame, start);
    }

    /// <summary>Writes the end of the current map, sorting its entries into canonical order.</summary>
    /// <exception cref="ArgumentException">Two entries have the same key.</exception>
    public void WriteEndMap() => End(isMap: true);

    /// <summary>Writes the start of an array of <paramref name="count"/> items.</summary>
    /// <param name="count">The number of items that follow, checked by <see cref="WriteEndArray"/>.</param>
    public void WriteStartArray(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Start(isMap: false, count);
    }

    /// <summary>Writes the end of the current array.</summary>
    public void WriteEndArray() => End(isMap: false);

    /// <summary>Writes an integer.</summary>
    /// <param name="value">The value.</param>
    public void WriteInt64(long value)
    {
        Value();
        WriteHeader(value < 0 ? DrislEncoding.NegativeInteger : DrislEncoding.UnsignedInteger, value < 0 ? (ulong)(-1 - value) : (ulong)value);
        Complete();
    }

    /// <summary>Writes a text string.</summary>
    /// <param name="value">The text.</param>
    /// <exception cref="ArgumentException">The text is not valid UTF-16 (a lone surrogate).</exception>
    public void WriteString(string value)
    {
        Value();
        WriteText(value);
        Complete();
    }

    /// <summary>Writes a byte string.</summary>
    /// <param name="value">The bytes.</param>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        Value();
        WriteRaw(DrislEncoding.ByteString, value);
        Complete();
    }

    /// <summary>Writes a CID link: tag 42 over <c>0x00</c> and the binary CID.</summary>
    /// <param name="cid">The CID.</param>
    public void WriteLink(Cid cid)
    {
        ArgumentNullException.ThrowIfNull(cid);
        Span<byte> binary = stackalloc byte[Cid.BinaryLength];
        cid.WriteBytes(binary);
        ((IDataModelSink)this).Link(binary);
    }

    /// <summary>Writes a boolean.</summary>
    /// <param name="value">The value.</param>
    public void WriteBoolean(bool value) => WriteSimple(value ? DrislEncoding.True : DrislEncoding.False);

    /// <summary>Writes a null.</summary>
    public void WriteNull() => WriteSimple(DrislEncoding.Null);

    // The encoded value, when the writer has no output.
    internal byte[] ToArray() => _depth == 0 && _length > 0 ? _buffer.AsSpan(0, _length).ToArray() : throw new InvalidOperationException("No complete value has been written.");

    void IDataModelSink.StartMap(int count) => Start(isMap: true, count);

    void IDataModelSink.Key(ReadOnlySpan<byte> utf8)
    {
        ref var frame = ref KeyFrame();
        var start = _length;
        WriteRaw(DrislEncoding.TextString, utf8);
        AddKey(ref frame, start);
    }

    void IDataModelSink.EndMap() => End(isMap: true);

    void IDataModelSink.StartArray(int count) => Start(isMap: false, count);

    void IDataModelSink.EndArray() => End(isMap: false);

    void IDataModelSink.Integer(long value) => WriteInt64(value);

    void IDataModelSink.String(ReadOnlySpan<byte> utf8)
    {
        Value();
        WriteRaw(DrislEncoding.TextString, utf8);
        Complete();
    }

    void IDataModelSink.Bytes(ReadOnlySpan<byte> bytes) => WriteBytes(bytes);

    void IDataModelSink.Link(ReadOnlySpan<byte> cid)
    {
        Value();
        var span = Reserve(2 + 2 + DrislEncoding.LinkLength);
        span[0] = 0xD8;
        span[1] = (byte)DrislEncoding.LinkTag;
        span[2] = 0x58;
        span[3] = DrislEncoding.LinkLength;
        span[4] = 0;
        cid.CopyTo(span[5..]);
        _length += 4 + DrislEncoding.LinkLength;
        Complete();
    }

    void IDataModelSink.Boolean(bool value) => WriteBoolean(value);

    void IDataModelSink.Null() => WriteNull();

    private void WriteSimple(byte value)
    {
        Value();
        Reserve(1)[0] = value;
        _length++;
        Complete();
    }

    // A container's header is written when it ends, when its count is known for certain; until then
    // one byte is reserved, which is the header of any map or array of fewer than 24 items.
    private void Start(bool isMap, int count)
    {
        Value();
        if (++_depth == _frames.Length) Array.Resize(ref _frames, _frames.Length * 2);
        var reserved = count < 0 ? 1 : DrislEncoding.HeaderLength((ulong)count);
        _frames[_depth] = new Frame { IsMap = isMap, Expected = count, HeaderAt = _length, HeaderLength = reserved, FirstEntry = _entryCount };
        Reserve(reserved);
        _length += reserved;
    }

    private void End(bool isMap)
    {
        ref var frame = ref _frames[_depth];
        if (_depth == 0 || frame.IsMap != isMap) throw new InvalidOperationException($"No {(isMap ? "map" : "array")} is open.");
        if (frame.KeyPending) throw new InvalidOperationException("The last map key has no value.");
        if (frame.Expected >= 0 && frame.Expected != frame.Count)
            throw new InvalidOperationException($"{frame.Count} items were written where {frame.Expected} were declared.");
        if (frame.Unsorted) Sort(frame);
        var header = DrislEncoding.HeaderLength((ulong)frame.Count);
        var body = frame.HeaderAt + frame.HeaderLength;
        if (header != frame.HeaderLength)
        {
            Reserve(header - frame.HeaderLength);
            _buffer.AsSpan(body, _length - body).CopyTo(_buffer.AsSpan(frame.HeaderAt + header));
            _length += header - frame.HeaderLength;
        }
        DrislEncoding.WriteHeader(_buffer.AsSpan(frame.HeaderAt), isMap ? DrislEncoding.Map : DrislEncoding.Array, (ulong)frame.Count);
        _entryCount = frame.FirstEntry;
        frame = default;
        _depth--;
        Complete();
    }

    private ref Frame KeyFrame()
    {
        ref var frame = ref _frames[_depth];
        if (!frame.IsMap || frame.KeyPending) throw new InvalidOperationException(frame.IsMap ? "The last map key has no value." : "Keys belong in maps.");
        return ref frame;
    }

    // Registers the key just written at start. A repeat of the key before it is taken back, so the
    // writer stays usable; a key smaller than the one before defers to Sort.
    private void AddKey(ref Frame frame, int start)
    {
        if (!frame.Unsorted && _entryCount > frame.FirstEntry)
        {
            var order = DrislEncoding.CompareKeys(KeyAt(_entries[_entryCount - 1]), KeyAt(start));
            if (order == 0)
            {
                _length = start;
                throw new ArgumentException("A map key is written twice.");
            }
            frame.Unsorted = order > 0;
        }
        if (_entryCount == _entries.Length) Array.Resize(ref _entries, _entries.Length * 2);
        _entries[_entryCount++] = start;
        frame.KeyPending = true;
        frame.Count++;
    }

    private void Value()
    {
        ref var frame = ref _frames[_depth];
        if (frame.IsMap && !frame.KeyPending) throw new InvalidOperationException("A map value needs its key first.");
        if (frame.IsMap) frame.KeyPending = false;
        else if (_depth > 0) frame.Count++;
    }

    // A top-level value is complete: it goes to the output.
    private void Complete()
    {
        if (_depth > 0 || _output is null) return;
        _output.Write(_buffer.AsSpan(0, _length));
        _length = 0;
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
    }

    private void Sort(in Frame frame)
    {
        var count = _entryCount - frame.FirstEntry;
        var bodyStart = _entries[frame.FirstEntry];
        var keys = new KeyOrder(_buffer, _length, _entries, _entryCount);
        var rented = count > 64 ? ArrayPool<int>.Shared.Rent(count) : null;
        var order = rented is null ? stackalloc int[count] : rented.AsSpan(0, count);
        for (var i = 0; i < count; i++) order[i] = frame.FirstEntry + i;
        order.Sort(keys);
        var sorted = ArrayPool<byte>.Shared.Rent(_length - bodyStart);
        var at = 0;
        for (var i = 0; i < count; i++)
        {
            if (i > 0 && keys.Compare(order[i - 1], order[i]) == 0) throw new ArgumentException("A map key is written twice.");
            var (start, end) = keys.Entry(order[i]);
            _buffer.AsSpan(start, end - start).CopyTo(sorted.AsSpan(at));
            at += end - start;
        }
        sorted.AsSpan(0, at).CopyTo(_buffer.AsSpan(bodyStart));
        ArrayPool<byte>.Shared.Return(sorted);
        if (rented is not null) ArrayPool<int>.Shared.Return(rented);
    }

    private ReadOnlySpan<byte> KeyAt(int offset) => KeyOrder.KeyAt(_buffer, _length, offset);

    private void WriteText(string value)
    {
        var count = System.Text.Encoding.UTF8.GetByteCount(value);
        var header = DrislEncoding.HeaderLength((ulong)count);
        var span = Reserve(header + count);
        if (Utf8.FromUtf16(value, span[header..], out _, out _, replaceInvalidSequences: false) != OperationStatus.Done)
            throw new ArgumentException("The text is not valid UTF-16.", nameof(value));
        DrislEncoding.WriteHeader(span, DrislEncoding.TextString, (ulong)count);
        _length += header + count;
    }

    private void WriteRaw(int major, ReadOnlySpan<byte> value)
    {
        var header = DrislEncoding.HeaderLength((ulong)value.Length);
        var span = Reserve(header + value.Length);
        DrislEncoding.WriteHeader(span, major, (ulong)value.Length);
        value.CopyTo(span[header..]);
        _length += header + value.Length;
    }

    private void WriteHeader(int major, ulong argument) => _length += DrislEncoding.WriteHeader(Reserve(9), major, argument);

    private Span<byte> Reserve(int count)
    {
        if (_buffer.Length - _length < count)
        {
            var grown = ArrayPool<byte>.Shared.Rent(Math.Max(256, Math.Max(_buffer.Length * 2, _length + count)));
            _buffer.AsSpan(0, _length).CopyTo(grown);
            if (_buffer.Length > 0) ArrayPool<byte>.Shared.Return(_buffer);
            _buffer = grown;
        }
        return _buffer.AsSpan(_length, count);
    }

    // Orders entry indices by their encoded keys.
    private readonly struct KeyOrder(byte[] buffer, int length, int[] entries, int entryCount) : IComparer<int>
    {
        public int Compare(int x, int y) => DrislEncoding.CompareKeys(KeyAt(buffer, length, entries[x]), KeyAt(buffer, length, entries[y]));

        public (int Start, int End) Entry(int index) => (entries[index], index + 1 < entryCount ? entries[index + 1] : length);

        // The encoded key (header and UTF-8) that starts at offset.
        public static ReadOnlySpan<byte> KeyAt(byte[] buffer, int length, int offset)
        {
            var header = DrislHeader.Read(buffer.AsSpan(0, length), offset);
            return buffer.AsSpan(offset, header.Length + (int)header.Argument);
        }
    }

    private struct Frame
    {
        public int Expected, Count, HeaderAt, HeaderLength, FirstEntry;
        public bool IsMap, KeyPending, Unsorted;
    }
}
