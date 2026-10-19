using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Data;

/// <summary>A link to content by CID, in its data-model form: <c>{"$link": "b…"}</c> in JSON, tag 42 in DRISL.</summary>
/// <remarks>
/// Lexicon <c>cid-link</c> fields use it; <c>format: cid</c> string fields use <see cref="ATProtoNet.Cid"/>
/// directly. See <see href="https://atproto.com/specs/data-model#link-and-cid-formats"/>.
/// </remarks>
[JsonConverter(typeof(CidLinkJsonConverter))]
public sealed record CidLink
{
    /// <summary>The linked CID.</summary>
    public required Cid Cid
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>Reads and writes a <see cref="CidLink"/> as <c>{"$link": "b…"}</c>.</summary>
public sealed class CidLinkJsonConverter : JsonConverter<CidLink>
{
    /// <inheritdoc />
    public override CidLink Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => new() { Cid = ReadLink(ref reader) };

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, CidLink value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(value);
        WriteLink(writer, value.Cid);
    }

    internal static Cid ReadLink(ref Utf8JsonReader reader)
    {
        if (reader.TokenType != JsonTokenType.StartObject || !reader.Read() || !reader.ValueTextEquals("$link"u8) || !reader.Read()
            || reader.TokenType != JsonTokenType.String || !Cid.TryParse(reader.GetString(), out var cid) || !reader.Read() || reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException("A link must be {\"$link\": \"<CID>\"} and nothing else, with a blessed CID.");
        return cid;
    }

    internal static void WriteLink(Utf8JsonWriter writer, Cid cid)
    {
        writer.WriteStartObject();
        writer.WriteString("$link"u8, cid.Value);
        writer.WriteEndObject();
    }
}
