using System.Buffers;
using System.Buffers.Text;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Data;

// Receives a data-model value as events. Each source (DRISL, JSON, a DataModelValue tree) feeds any sink
// (DrislWriter, JsonSink, DataModelTreeBuilder), always through CheckedSink, so every conversion applies
// the same rules. Strings and keys arrive as valid UTF-8, links as 36 binary CID bytes.
internal interface IDataModelSink
{
    // count is -1 when the source does not know it yet (JSON, indefinite-length CBOR).
    void StartMap(int count);
    void Key(ReadOnlySpan<byte> utf8);
    void EndMap();
    void StartArray(int count);
    void EndArray();
    void Integer(long value);
    void String(ReadOnlySpan<byte> utf8);
    void Bytes(ReadOnlySpan<byte> bytes);
    void Link(ReadOnlySpan<byte> cid);
    void Boolean(bool value);
    void Null();
}

// The data-model rules a format alone does not carry, applied between source and sink: the depth limit
// in both encodings, $type a non-empty string, a {"$type": "blob"} map with a ref link, a mimeType
// string and an integer size, and no $link or $bytes map keys, which the JSON form could not tell from a
// link or bytes (atproto-interop-tests data-model-invalid.json). The finer blob rules (a raw codec, no
// other keys) are BlobRef's, so a value with an unusual blob still converts.
internal struct CheckedSink<TSink>(TSink inner) : IDataModelSink where TSink : IDataModelSink
{
    private TSink _inner = inner;
    private Levels _levels;
    private int _depth;
    private Field _field;

    public readonly TSink Inner => _inner;

    public void StartMap(int count)
    {
        Container();
        _inner.StartMap(count);
    }

    public void Key(ReadOnlySpan<byte> utf8)
    {
        _field = utf8.SequenceEqual("$type"u8) ? Field.Type
            : utf8.SequenceEqual("ref"u8) ? Field.Ref
            : utf8.SequenceEqual("mimeType"u8) ? Field.MimeType
            : utf8.SequenceEqual("size"u8) ? Field.Size
            : utf8.SequenceEqual("$link"u8) || utf8.SequenceEqual("$bytes"u8) ? throw new FormatException("A map key $link or $bytes is reserved for links and bytes in the JSON form.")
            : Field.None;
        _inner.Key(utf8);
    }

    public void EndMap()
    {
        const Flags blob = Flags.Blob | Flags.Ref | Flags.MimeType | Flags.Size;
        if ((_levels[_depth] & Flags.Blob) != 0 && (_levels[_depth] & blob) != blob)
            throw new FormatException("A {\"$type\": \"blob\"} map needs a ref link, a mimeType string and an integer size.");
        _depth--;
        _inner.EndMap();
    }

    public void StartArray(int count)
    {
        Container();
        _inner.StartArray(count);
    }

    public void EndArray()
    {
        _depth--;
        _inner.EndArray();
    }

    public void Integer(long value)
    {
        Scalar(Field.Size);
        _inner.Integer(value);
    }

    public void String(ReadOnlySpan<byte> utf8)
    {
        if (_field == Field.Type)
        {
            if (utf8.IsEmpty) throw TypeNotAString();
            _levels[_depth] |= utf8.SequenceEqual("blob"u8) ? Flags.Type | Flags.Blob : Flags.Type;
            _field = Field.None;
        }
        Scalar(Field.MimeType);
        _inner.String(utf8);
    }

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        Wrapper();
        Scalar(Field.None);
        _inner.Bytes(bytes);
    }

    public void Link(ReadOnlySpan<byte> cid)
    {
        Wrapper();
        Scalar(Field.Ref);
        _inner.Link(cid);
    }

    public void Boolean(bool value)
    {
        Scalar(Field.None);
        _inner.Boolean(value);
    }

    public void Null()
    {
        Scalar(Field.None);
        _inner.Null();
    }

    // A scalar is the value of the pending key; it satisfies that blob field when it is of the right kind.
    private void Scalar(Field satisfies)
    {
        if (_field == Field.Type) throw TypeNotAString();
        if (_field != Field.None && _field == satisfies) _levels[_depth] |= (Flags)(1 << (int)satisfies);
        _field = Field.None;
    }

    private void Container()
    {
        Scalar(Field.None);
        if (++_depth > DrislLimits.MaxDepth) throw new DataLimitExceededException(nameof(DrislLimits.MaxDepth), $"The value nests deeper than {DrislLimits.MaxDepth} levels.");
        _levels[_depth] = 0;
    }

    private readonly void Wrapper()
    {
        if (_depth + 1 > DrislLimits.MaxDepth) throw new DataLimitExceededException(nameof(DrislLimits.MaxDepth), $"The value nests deeper than {DrislLimits.MaxDepth} levels.");
    }

    private static FormatException TypeNotAString() => new("$type must be a non-empty string.");

    // Field values double as the bit index of their Flags.
    private enum Field : byte { None, Ref, MimeType, Size, Type }

    [Flags]
    private enum Flags : byte { Ref = 1 << 1, MimeType = 1 << 2, Size = 1 << 3, Type = 1 << 4, Blob = 1 << 5 }

    [InlineArray(DrislLimits.MaxDepth + 1)]
    private struct Levels
    {
        private Flags _level;
    }
}

// Writes the JSON form: {"$link": "b…"} for links and {"$bytes": "<unpadded base64>"} for bytes.
internal readonly struct JsonSink(Utf8JsonWriter writer) : IDataModelSink
{
    public void StartMap(int count) => writer.WriteStartObject();

    public void Key(ReadOnlySpan<byte> utf8) => writer.WritePropertyName(utf8);

    public void EndMap() => writer.WriteEndObject();

    public void StartArray(int count) => writer.WriteStartArray();

    public void EndArray() => writer.WriteEndArray();

    public void Integer(long value) => writer.WriteNumberValue(value);

    public void String(ReadOnlySpan<byte> utf8) => writer.WriteStringValue(utf8);

    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        // Two quotes around unpadded base64, whose alphabet needs no JSON escaping.
        var length = Base64.GetMaxEncodedToUtf8Length(bytes.Length) + 2;
        var rented = length > 256 ? ArrayPool<byte>.Shared.Rent(length) : null;
        var quoted = rented ?? stackalloc byte[256];
        Base64.EncodeToUtf8(bytes, quoted[1..], out _, out var written);
        written = quoted.Slice(1, written).TrimEnd((byte)'=').Length;
        quoted[0] = quoted[written + 1] = (byte)'"';
        writer.WriteStartObject();
        writer.WritePropertyName("$bytes"u8);
        writer.WriteRawValue(quoted[..(written + 2)], skipInputValidation: true);
        writer.WriteEndObject();
        if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
    }

    public void Link(ReadOnlySpan<byte> cid)
    {
        Span<char> text = stackalloc char[Cid.StringLength];
        text[0] = 'b';
        Base32Lower.Encode(cid, text[1..]);
        writer.WriteStartObject();
        writer.WriteString("$link"u8, text);
        writer.WriteEndObject();
    }

    public void Boolean(bool value) => writer.WriteBooleanValue(value);

    public void Null() => writer.WriteNullValue();
}

// Builds a DataModelValue tree.
internal sealed class DataModelTreeBuilder : IDataModelSink
{
    private readonly List<Container> _open = [];
    private DataModelValue? _result;

    public DataModelValue Result => _result ?? throw new InvalidOperationException("No complete value was built.");

    public void StartMap(int count) => _open.Add(new Container(isMap: true, count));

    public void Key(ReadOnlySpan<byte> utf8) => _open[^1].Key = Encoding.UTF8.GetString(utf8);

    public void EndMap() => Close();

    public void StartArray(int count) => _open.Add(new Container(isMap: false, count));

    public void EndArray() => Close();

    public void Integer(long value) => Add(new DataModelInteger(value));

    public void String(ReadOnlySpan<byte> utf8) => Add(new DataModelString(Encoding.UTF8.GetString(utf8)));

    public void Bytes(ReadOnlySpan<byte> bytes) => Add(new DataModelBytes(bytes.ToArray()));

    public void Link(ReadOnlySpan<byte> cid) => Add(new DataModelLink(Cid.FromBytes(cid)));

    public void Boolean(bool value) => Add(new DataModelBoolean(value));

    public void Null() => Add(DataModelNull.Instance);

    private void Close()
    {
        var container = _open[^1];
        _open.RemoveAt(_open.Count - 1);
        Add(container.Entries is { } entries ? DataModelMap.FromParsedEntries(entries) : new DataModelArray(container.Items!.ToArray(), owned: true));
    }

    private void Add(DataModelValue value)
    {
        if (_open.Count == 0) _result = value;
        else if (_open[^1] is { Entries: { } entries } map) entries.Add(new(map.Key!, value));
        else _open[^1].Items!.Add(value);
    }

    // A declared count is a hint from the input, so it sizes the first allocation only up to a few items.
    private sealed class Container(bool isMap, int count)
    {
        public List<KeyValuePair<string, DataModelValue>>? Entries { get; } = isMap ? new(Math.Clamp(count, 0, 16)) : null;
        public List<DataModelValue>? Items { get; } = isMap ? null : new(Math.Clamp(count, 0, 16));
        public string? Key { get; set; }
    }
}
