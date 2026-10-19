using System.Buffers;
using System.Text;
using System.Text.Unicode;

namespace ATProtoNet.Data;

/// <summary>A forward-only reader of one DRISL-CBOR value, strict by default.</summary>
/// <remarks>
/// Every failure is a <see cref="FormatException"/> (a <see cref="DataLimitExceededException"/> for a
/// limit), and nesting is counted by the reader itself, also while skipping. Strict mode enforces every
/// rule of <see href="https://dasl.ing/drisl.html">DRISL</see>; lenient mode accepts non-canonical but
/// well-formed CBOR and still refuses floats, non-string keys, duplicate keys and malformed links. See
/// <see href="https://atproto.com/specs/data-model"/>.
/// </remarks>
public sealed class DrislReader
{
    private readonly ReadOnlyMemory<byte> _data;
    private readonly DrislReaderOptions _options;
    private readonly bool _strict;
    private Frame[] _frames = new Frame[4];
    private int _depth, _position, _peekedAt = -1;
    private DrislHeader _peeked;

    /// <summary>Creates a reader over one encoded value.</summary>
    /// <param name="data">The DRISL bytes.</param>
    /// <param name="options">The mode and depth limit; strict with <see cref="DrislLimits.MaxDepth"/> when omitted.</param>
    public DrislReader(ReadOnlyMemory<byte> data, DrislReaderOptions? options = null)
    {
        _data = data;
        _options = options ?? DrislReaderOptions.Default;
        _strict = _options.Mode == DrislMode.Strict;
        _frames[0].Count = 1;
    }

    internal int Position => _position;

    /// <summary>The kind of the next item, or <see cref="DrislValueKind.End"/> when the current map or array has no more.</summary>
    /// <returns>The kind; a map key is a <see cref="DrislValueKind.String"/>.</returns>
    /// <exception cref="FormatException">The next item is malformed or not allowed.</exception>
    public DrislValueKind PeekKind() => AtEnd(ref _frames[_depth]) ? DrislValueKind.End : KindOf(Peek());

    /// <summary>Reads the start of a map.</summary>
    /// <returns>The number of entries, or -1 for an indefinite-length map (lenient mode only).</returns>
    /// <exception cref="FormatException">The next item is not a map, or nests too deep.</exception>
    public int ReadStartMap() => Push(DrislValueKind.Map);

    /// <summary>Reads the next map key, checking its order and uniqueness.</summary>
    /// <returns>The key.</returns>
    /// <exception cref="FormatException">The key is not a string, out of canonical order (strict) or a duplicate.</exception>
    public string ReadKey() => Encoding.UTF8.GetString(ReadKeyUtf8());

    /// <summary>Reads the end of the current map.</summary>
    /// <exception cref="FormatException">Entries remain.</exception>
    public void ReadEndMap() => Pop(isMap: true);

    /// <summary>Reads the start of an array.</summary>
    /// <returns>The number of items, or -1 for an indefinite-length array (lenient mode only).</returns>
    /// <exception cref="FormatException">The next item is not an array, or nests too deep.</exception>
    public int ReadStartArray() => Push(DrislValueKind.Array);

    /// <summary>Reads the end of the current array.</summary>
    /// <exception cref="FormatException">Items remain.</exception>
    public void ReadEndArray() => Pop(isMap: false);

    /// <summary>Reads an integer.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The next item is not an integer.</exception>
    public long ReadInt64()
    {
        var header = Begin(DrislValueKind.Integer);
        return header.Major == DrislEncoding.UnsignedInteger ? (long)header.Argument : -1 - (long)header.Argument;
    }

    /// <summary>Reads a text string.</summary>
    /// <returns>The text.</returns>
    /// <exception cref="FormatException">The next item is not a text string, or (strict) not valid UTF-8.</exception>
    public string ReadString() => Encoding.UTF8.GetString(ReadStringUtf8());

    /// <summary>Reads a byte string.</summary>
    /// <returns>The bytes: a slice of the input unless the string has an indefinite length.</returns>
    /// <exception cref="FormatException">The next item is not a byte string.</exception>
    public ReadOnlyMemory<byte> ReadBytes()
    {
        var header = Begin(DrislValueKind.Bytes);
        CheckWrapperDepth();
        return header.Indefinite ? ReadChunks(DrislEncoding.ByteString) : Slice(header.Argument);
    }

    /// <summary>Reads a CID link.</summary>
    /// <returns>The CID.</returns>
    /// <exception cref="FormatException">The next item is not a tag-42 link to a blessed CID.</exception>
    public Cid ReadLink() => Cid.FromBytes(ReadLinkBinary());

    /// <summary>Reads a boolean.</summary>
    /// <returns>The value.</returns>
    /// <exception cref="FormatException">The next item is not a boolean.</exception>
    public bool ReadBoolean() => Begin(DrislValueKind.Boolean).Info == (DrislEncoding.True & 0x1F);

    /// <summary>Reads a null.</summary>
    /// <exception cref="FormatException">The next item is not a null.</exception>
    public void ReadNull() => Begin(DrislValueKind.Null);

    /// <summary>Skips the next value, checking it exactly as reading it would, without recursion.</summary>
    /// <exception cref="FormatException">The value is malformed or nests too deep.</exception>
    public void SkipValue()
    {
        var depth = _depth;
        do
        {
            ref var frame = ref _frames[_depth];
            if (_depth > depth && AtEnd(ref frame)) Pop(frame.IsMap);
            else if (_depth > depth && frame.AtKey) ReadKeyUtf8();
            else
                switch (PeekKind())
                {
                    case DrislValueKind.Map: ReadStartMap(); break;
                    case DrislValueKind.Array: ReadStartArray(); break;
                    case DrislValueKind.Integer: ReadInt64(); break;
                    case DrislValueKind.String: ReadStringUtf8(); break;
                    case DrislValueKind.Bytes: ReadBytes(); break;
                    case DrislValueKind.Link: ReadLinkBinary(); break;
                    case DrislValueKind.Boolean: ReadBoolean(); break;
                    case DrislValueKind.Null: ReadNull(); break;
                    case DrislValueKind.End: throw new FormatException("There is no value to skip.");
                }
        }
        while (_depth > depth);
    }

    /// <summary>Checks that the top-level value has been read and nothing follows it.</summary>
    /// <exception cref="FormatException">Bytes follow the value.</exception>
    public void ReadEnd()
    {
        if (_depth != 0 || _frames[0].Items != 1) throw new InvalidOperationException("The top-level value has not been read.");
        if (_position != _data.Length) throw new FormatException("Bytes follow the top-level DRISL value.");
    }

    internal ReadOnlySpan<byte> ReadKeyUtf8()
    {
        ref var frame = ref _frames[_depth];
        var header = Begin(DrislValueKind.String, key: true);
        var start = _position - header.Length;
        var key = ReadText(header);
        if (_strict) CheckOrder(ref frame, start, key);
        else CheckUnique(ref frame, Encoding.UTF8.GetString(key));
        return key;
    }

    internal ReadOnlySpan<byte> ReadStringUtf8() => ReadText(Begin(DrislValueKind.String));

    internal ReadOnlySpan<byte> ReadLinkBinary()
    {
        Begin(DrislValueKind.Link);
        CheckWrapperDepth();
        var span = _data.Span;
        var inner = DrislHeader.Read(span, _position);
        if (inner is not { Major: DrislEncoding.ByteString, Indefinite: false, Argument: DrislEncoding.LinkLength } || _strict && !inner.IsShortest)
            throw new FormatException("A tag-42 link must wrap a 37-byte string: 0x00 and a blessed binary CID.");
        _position += inner.Length;
        var link = Slice(DrislEncoding.LinkLength).Span;
        return link[0] == 0 && Cid.TryFromBytes(link[1..], out _) ? link[1..]
            : throw new FormatException("A tag-42 link must hold 0x00 and a blessed CID (CIDv1, dag-cbor or raw, sha2-256).");
    }

    // Strict: every header is shortest, so the encoded keys compare as the keys do, and each must follow the last.
    private void CheckOrder(ref Frame frame, int start, ReadOnlySpan<byte> key)
    {
        var span = _data.Span;
        if (frame.PreviousKey >= 0)
        {
            var order = DrislEncoding.CompareKeys(span.Slice(frame.PreviousKey, frame.PreviousKeyLength), span[start.._position]);
            if (order == 0) throw DuplicateKey(Encoding.UTF8.GetString(key));
            if (order > 0) throw new FormatException("Map keys are not in canonical DRISL order.");
        }
        (frame.PreviousKey, frame.PreviousKeyLength) = (start, _position - start);
    }

    // Lenient: in any order and any header form, so the decoded keys are what must not repeat.
    private static void CheckUnique(ref Frame frame, string key)
    {
        if (frame.FirstKey is null)
        {
            frame.FirstKey = key;
            return;
        }
        frame.Keys ??= new(StringComparer.Ordinal) { frame.FirstKey };
        if (!frame.Keys.Add(key)) throw DuplicateKey(key);
    }

    private int Push(DrislValueKind kind)
    {
        var header = Begin(kind);
        if (_depth + 1 > _options.MaxDepth) throw DepthExceeded();
        var isMap = kind == DrislValueKind.Map;
        var count = -1;
        if (!header.Indefinite)
        {
            // Every item takes at least a byte: a longer declaration cannot be true, and is never trusted.
            if (header.Argument > (ulong)(_data.Length - _position) / (isMap ? 2u : 1u))
                throw new FormatException($"The declared {(isMap ? "map" : "array")} length exceeds the input.");
            count = (int)header.Argument;
        }
        if (++_depth == _frames.Length) Array.Resize(ref _frames, _frames.Length * 2);
        _frames[_depth] = new Frame { IsMap = isMap, Count = count < 0 ? -1 : isMap ? count * 2 : count, PreviousKey = -1 };
        return count;
    }

    private void Pop(bool isMap)
    {
        ref var frame = ref _frames[_depth];
        if (_depth == 0 || frame.IsMap != isMap) throw new InvalidOperationException($"No {(isMap ? "map" : "array")} is open.");
        if (!AtEnd(ref frame)) throw new FormatException($"The {(isMap ? "map" : "array")} has more items.");
        if (frame.Count < 0) _position++;
        frame = default;
        _depth--;
    }

    // Starts the next item: checks the key/value position and the kind, and consumes the header.
    private DrislHeader Begin(DrislValueKind kind, bool key = false)
    {
        ref var frame = ref _frames[_depth];
        if (frame.AtKey != key) throw new InvalidOperationException(key ? "A map value comes next, not a key." : "A map key comes next.");
        if (AtEnd(ref frame)) throw new FormatException($"Expected {kind}, found the end of the {(_depth == 0 ? "input" : frame.IsMap ? "map" : "array")}.");
        var header = Peek();
        var actual = KindOf(header);
        if (actual != kind) throw new FormatException(key ? "Map keys must be text strings." : $"Expected {kind}, found {actual}.");
        frame.Items++;
        _position += header.Length;
        return header;
    }

    // The next item's header, after skipping unknown tags (lenient) and refusing what DRISL forbids.
    private DrislHeader Peek()
    {
        if (_peekedAt == _position) return _peeked;
        var span = _data.Span;
        while (true)
        {
            var header = DrislHeader.Read(span, _position);
            switch (header)
            {
                case { Major: DrislEncoding.Simple, Info: >= 25 and <= 27 }: throw DataModelNumbers.NotAnInteger();
                case { Major: DrislEncoding.Simple, Info: 31 }: throw new FormatException("A CBOR break outside an indefinite-length item.");
                case { Major: DrislEncoding.Simple, Info: < 20 or > 22 }: throw new FormatException("Only false, true and null are simple values in the data model.");
                case { Major: DrislEncoding.UnsignedInteger or DrislEncoding.NegativeInteger, Argument: > long.MaxValue }: throw DataModelNumbers.OutOfRange();
                case { Major: DrislEncoding.Tag } when header.Argument != DrislEncoding.LinkTag && !_strict:
                    _position += header.Length;
                    continue;
                case { Major: DrislEncoding.Tag } when header.Argument != DrislEncoding.LinkTag:
                    throw new FormatException($"CBOR tag {header.Argument} is not allowed; DRISL has only tag 42.");
            }
            if (_strict && !header.IsShortest)
                throw new FormatException(header.Indefinite ? "Indefinite lengths are not allowed in DRISL." : "A CBOR integer, length or tag is not in its shortest form.");
            _peekedAt = _position;
            return _peeked = header;
        }
    }

    private bool AtEnd(ref Frame frame)
    {
        if (frame.Count >= 0) return frame.Items == frame.Count;
        var span = _data.Span;
        if (_position >= span.Length) throw DrislHeader.Truncated();
        if (span[_position] != DrislEncoding.Break) return false;
        if (frame.AtValue) throw new FormatException("An indefinite-length map ends after a key.");
        return true;
    }

    private static DrislValueKind KindOf(DrislHeader header) => header.Major switch
    {
        DrislEncoding.UnsignedInteger or DrislEncoding.NegativeInteger => DrislValueKind.Integer,
        DrislEncoding.ByteString => DrislValueKind.Bytes,
        DrislEncoding.TextString => DrislValueKind.String,
        DrislEncoding.Array => DrislValueKind.Array,
        DrislEncoding.Map => DrislValueKind.Map,
        DrislEncoding.Tag => DrislValueKind.Link,
        _ => header.Info == (DrislEncoding.Null & 0x1F) ? DrislValueKind.Null : DrislValueKind.Boolean,
    };

    private ReadOnlySpan<byte> ReadText(DrislHeader header)
    {
        var text = header.Indefinite ? ReadChunks(DrislEncoding.TextString).Span : Slice(header.Argument).Span;
        if (Utf8.IsValid(text)) return text;
        if (_strict) throw new FormatException("A DRISL text string is not valid UTF-8.");
        return Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(text));
    }

    private ReadOnlyMemory<byte> Slice(ulong length)
    {
        if (length > (ulong)(_data.Length - _position)) throw new FormatException("The declared string length exceeds the input.");
        var slice = _data.Slice(_position, (int)length);
        _position += (int)length;
        return slice;
    }

    // The chunks of an indefinite-length string (lenient mode): definite strings of the same type.
    private ReadOnlyMemory<byte> ReadChunks(int major)
    {
        var joined = new ArrayBufferWriter<byte>();
        var span = _data.Span;
        while (true)
        {
            var chunk = DrislHeader.Read(span, _position);
            _position += chunk.Length;
            if (chunk is { Major: DrislEncoding.Simple, Info: 31 }) return joined.WrittenMemory;
            if (chunk.Major != major || chunk.Indefinite) throw new FormatException("An indefinite-length string holds a chunk that is not a definite string of its type.");
            joined.Write(Slice(chunk.Argument).Span);
        }
    }

    private void CheckWrapperDepth()
    {
        // A link or byte string is a level in its JSON form ({"$link"}, {"$bytes"}), so both forms accept the same values.
        if (_depth + 1 > _options.MaxDepth) throw DepthExceeded();
    }

    private DataLimitExceededException DepthExceeded() =>
        new(nameof(DrislReaderOptions.MaxDepth), $"The DRISL value nests deeper than {_options.MaxDepth} levels.");

    private static FormatException DuplicateKey(string key) => new($"The map key \"{key}\" occurs more than once.");

    private struct Frame
    {
        public int Count, Items, PreviousKey, PreviousKeyLength;
        public bool IsMap;
        public string? FirstKey;
        public HashSet<string>? Keys;

        public readonly bool AtKey => IsMap && (Items & 1) == 0;

        public readonly bool AtValue => IsMap && (Items & 1) == 1;
    }
}
