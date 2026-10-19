using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;

namespace ATProtoNet.Data;

// The sources of data-model events: DRISL, JSON and DataModelValue trees. Each recurses once per level;
// the depth limit (DRISL: the reader; JSON and trees: CheckedSink) stops it after 64 levels.
internal static class DataModelSources
{
    public static void Drisl<TSink>(DrislReader reader, ref TSink sink) where TSink : IDataModelSink
    {
        switch (reader.PeekKind())
        {
            case DrislValueKind.Map:
                sink.StartMap(reader.ReadStartMap());
                while (reader.PeekKind() != DrislValueKind.End)
                {
                    sink.Key(reader.ReadKeyUtf8());
                    Drisl(reader, ref sink);
                }
                reader.ReadEndMap();
                sink.EndMap();
                break;
            case DrislValueKind.Array:
                sink.StartArray(reader.ReadStartArray());
                while (reader.PeekKind() != DrislValueKind.End) Drisl(reader, ref sink);
                reader.ReadEndArray();
                sink.EndArray();
                break;
            case DrislValueKind.Integer: sink.Integer(reader.ReadInt64()); break;
            case DrislValueKind.String: sink.String(reader.ReadStringUtf8()); break;
            case DrislValueKind.Bytes: sink.Bytes(reader.ReadBytes().Span); break;
            case DrislValueKind.Link: sink.Link(reader.ReadLinkBinary()); break;
            case DrislValueKind.Boolean: sink.Boolean(reader.ReadBoolean()); break;
            case DrislValueKind.Null: reader.ReadNull(); sink.Null(); break;
            case DrislValueKind.End: throw new FormatException("A value was expected.");
        }
    }

    // Reads the value at the reader's current token, leaving the reader on its last token.
    public static void Json<TSink>(ref Utf8JsonReader reader, ref TSink sink) where TSink : IDataModelSink
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                JsonObject(ref reader, ref sink);
                break;
            case JsonTokenType.StartArray:
                sink.StartArray(-1);
                while (Next(ref reader) != JsonTokenType.EndArray) Json(ref reader, ref sink);
                sink.EndArray();
                break;
            case JsonTokenType.String: sink.String(Unescaped(ref reader)); break;
            case JsonTokenType.Number: sink.Integer(DataModelNumbers.FromJson(reader.ValueSpan)); break;
            case JsonTokenType.True: sink.Boolean(true); break;
            case JsonTokenType.False: sink.Boolean(false); break;
            case JsonTokenType.Null: sink.Null(); break;
            default: throw new FormatException($"A JSON value was expected, found {reader.TokenType}.");
        }
    }

    public static void Tree<TSink>(DataModelValue value, ref TSink sink) where TSink : IDataModelSink
    {
        switch (value)
        {
            case DataModelMap map:
                sink.StartMap(map.Count);
                foreach (var (key, item) in map.Entries)
                {
                    Text(key, isKey: true, ref sink);
                    Tree(item, ref sink);
                }
                sink.EndMap();
                break;
            case DataModelArray array:
                sink.StartArray(array.Count);
                foreach (var item in array) Tree(item, ref sink);
                sink.EndArray();
                break;
            case DataModelInteger integer: sink.Integer(integer.Value); break;
            case DataModelString text: Text(text.Value, isKey: false, ref sink); break;
            case DataModelBytes bytes: sink.Bytes(bytes.Value.Span); break;
            case DataModelLink link:
                Span<byte> cid = stackalloc byte[Cid.BinaryLength];
                link.Cid.WriteBytes(cid);
                sink.Link(cid);
                break;
            case DataModelBoolean boolean: sink.Boolean(boolean.Value); break;
            case DataModelNull: sink.Null(); break;
        }
    }

    private static void Text<TSink>(string value, bool isKey, ref TSink sink) where TSink : IDataModelSink
    {
        var length = Encoding.UTF8.GetByteCount(value);
        var rented = length > 256 ? ArrayPool<byte>.Shared.Rent(length) : null;
        var utf8 = (rented ?? stackalloc byte[256])[..length];
        if (Utf8.FromUtf16(value, utf8, out _, out _, replaceInvalidSequences: false) != OperationStatus.Done)
            throw new FormatException("A data-model string is not valid UTF-16.");
        if (isKey) sink.Key(utf8);
        else sink.String(utf8);
        if (rented is not null) ArrayPool<byte>.Shared.Return(rented);
    }

    // An object is a map, unless its first property is $link or $bytes: then it is a link or bytes and has
    // no other property. A later $link or $bytes key is refused by CheckedSink.
    private static void JsonObject<TSink>(ref Utf8JsonReader reader, ref TSink sink) where TSink : IDataModelSink
    {
        if (Next(ref reader) == JsonTokenType.EndObject)
        {
            sink.StartMap(0);
            sink.EndMap();
            return;
        }
        if (reader.ValueTextEquals("$link"u8))
        {
            Span<byte> cid = stackalloc byte[Cid.BinaryLength];
            if (Next(ref reader) != JsonTokenType.String || !Cid.TryParse(Unescaped(ref reader), out var link))
                throw new FormatException("$link must hold a blessed CID string.");
            link.WriteBytes(cid);
            EndOfSpecial(ref reader, "$link");
            sink.Link(cid);
            return;
        }
        if (reader.ValueTextEquals("$bytes"u8))
        {
            if (Next(ref reader) != JsonTokenType.String) throw new FormatException("$bytes must hold a base64 string.");
            var bytes = FromBase64(Unescaped(ref reader));
            EndOfSpecial(ref reader, "$bytes");
            sink.Bytes(bytes);
            return;
        }
        sink.StartMap(-1);
        do
        {
            sink.Key(Unescaped(ref reader));
            Next(ref reader);
            Json(ref reader, ref sink);
        }
        while (Next(ref reader) == JsonTokenType.PropertyName);
        sink.EndMap();
    }

    private static void EndOfSpecial(ref Utf8JsonReader reader, string name)
    {
        if (Next(ref reader) != JsonTokenType.EndObject) throw new FormatException($"A {name} object has no other property.");
    }

    // Standard base64, with or without padding (DAT-46).
    private static byte[] FromBase64(ReadOnlySpan<byte> text) =>
        StrictBase64.TryDecode(text, url: false, allowPadding: true, out var bytes) ? bytes : throw NotBase64();

    private static FormatException NotBase64() => new("$bytes must hold standard base64, padded or not.");

    private static JsonTokenType Next(ref Utf8JsonReader reader) =>
        reader.Read() ? reader.TokenType : throw new FormatException("The JSON ends inside a value.");

    private static ReadOnlySpan<byte> Unescaped(ref Utf8JsonReader reader)
    {
        if (!reader.ValueIsEscaped) return reader.ValueSpan;
        var unescaped = new byte[reader.ValueSpan.Length];
        return unescaped.AsSpan(0, reader.CopyString(unescaped));
    }
}
