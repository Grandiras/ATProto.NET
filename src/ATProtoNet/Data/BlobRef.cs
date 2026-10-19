using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Data;

/// <summary>A reference to a blob: <c>{"$type": "blob", "ref": {"$link": …}, "mimeType": …, "size": …}</c>.</summary>
/// <remarks>
/// The legacy form <c>{"cid": "…", "mimeType": "…"}</c> is read into a reference with
/// <see cref="IsLegacy"/> set and no <see cref="Size"/>, and written back in that form so a record
/// survives a read-modify-write; the SDK never creates one. See
/// <see href="https://atproto.com/specs/data-model#blob-type"/>.
/// </remarks>
[JsonConverter(typeof(BlobRefJsonConverter))]
public sealed record BlobRef
{
    /// <summary>Creates a reference; set <see cref="Ref"/>, <see cref="MimeType"/> and <see cref="Size"/>.</summary>
    public BlobRef() { }

    private BlobRef(bool isLegacy) => IsLegacy = isLegacy;

    /// <summary>The link to the blob's bytes; its codec is <see cref="CidCodec.Raw"/>.</summary>
    /// <exception cref="ArgumentException">The link's codec is not raw.</exception>
    public required CidLink Ref
    {
        get;
        init => field = value is null ? throw new ArgumentNullException(nameof(value))
            : IsLegacy || value.Cid.Codec == CidCodec.Raw ? value
            : throw new ArgumentException("A blob's ref must be a raw-codec CID.", nameof(value));
    }

    /// <summary>The blob's media type, never empty.</summary>
    public required string MimeType
    {
        get;
        init => field = string.IsNullOrEmpty(value) ? throw new ArgumentException("A blob's mimeType must not be empty.", nameof(value)) : value;
    }

    /// <summary>The blob's length in bytes; <see langword="null"/> only for a legacy blob.</summary>
    public long? Size
    {
        get;
        init => field = value is null or >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "A blob's size must not be negative.");
    }

    /// <summary>Whether this reference was read from the legacy <c>{"cid", "mimeType"}</c> form.</summary>
    public bool IsLegacy { get; }

    internal static BlobRef Legacy(Cid cid, string mimeType) => new(isLegacy: true) { Ref = new() { Cid = cid }, MimeType = mimeType };
}

/// <summary>Reads and writes a <see cref="BlobRef"/> in the data model's blob form, or the legacy form it was read from.</summary>
public sealed class BlobRefJsonConverter : JsonConverter<BlobRef>
{
    /// <inheritdoc />
    public override BlobRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject) throw Invalid();
        string? type = null, mimeType = null, legacyCid = null;
        Cid? link = null;
        long? size = null;
        var properties = 0;
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            properties++;
            if (reader.ValueTextEquals("ref"u8))
            {
                reader.Read();
                link = CidLinkJsonConverter.ReadLink(ref reader);
            }
            else if (reader.ValueTextEquals("size"u8))
            {
                reader.Read();
                size = reader.TokenType == JsonTokenType.Number ? FromJson(reader.ValueSpan) : throw Invalid();
            }
            else
            {
                var name = reader.GetString();
                reader.Read();
                var text = reader.TokenType == JsonTokenType.String ? reader.GetString() : throw Invalid();
                switch (name)
                {
                    case "$type": type = text; break;
                    case "mimeType": mimeType = text; break;
                    case "cid": legacyCid = text; break;
                    default: throw Invalid();
                }
            }
        }
        if (string.IsNullOrEmpty(mimeType)) throw Invalid();
        if (type == "blob" && properties == 4 && link is { Codec: CidCodec.Raw } && size >= 0)
            return new() { Ref = new() { Cid = link }, MimeType = mimeType, Size = size };
        if (type is null && properties == 2 && Cid.TryParse(legacyCid, out var cid)) return BlobRef.Legacy(cid, mimeType);
        throw Invalid();
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BlobRef value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        writer.WriteStartObject();
        if (value.IsLegacy)
        {
            writer.WriteString("cid"u8, value.Ref.Cid.Value);
            writer.WriteString("mimeType"u8, value.MimeType);
        }
        else
        {
            writer.WriteString("$type"u8, "blob"u8);
            writer.WritePropertyName("ref"u8);
            CidLinkJsonConverter.WriteLink(writer, value.Ref.Cid);
            writer.WriteString("mimeType"u8, value.MimeType);
            writer.WriteNumber("size"u8, value.Size ?? throw new JsonException("A blob needs its size; only a legacy blob has none."));
        }
        writer.WriteEndObject();
    }

    private static long FromJson(ReadOnlySpan<byte> number)
    {
        try
        {
            return DataModelNumbers.FromJson(number);
        }
        catch (FormatException e)
        {
            throw new JsonException(e.Message, e);
        }
    }

    private static JsonException Invalid() => new(
        "A blob must be {\"$type\": \"blob\", \"ref\": {\"$link\": <raw CID>}, \"mimeType\": <non-empty>, \"size\": <integer ≥ 0>}, or the legacy {\"cid\", \"mimeType\"}.");
}
