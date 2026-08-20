using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Serialization;

/// <summary>
/// Converts a <see cref="byte"/> array to and from the AT Protocol JSON data model's
/// representation of a Lexicon <c>bytes</c> value: <c>{ "$bytes": "&lt;base64&gt;" }</c>.
/// </summary>
/// <remarks>
/// <para>Lexicon <c>bytes</c> is a first-class CBOR byte string, and the JSON data model wraps
/// it in a single-key object rather than emitting a bare string, so that a byte string and a
/// text string stay distinguishable across the two encodings. Permissioned-space commits carry
/// four of them (<c>hash</c>, <c>ikm</c>, <c>sig</c>, <c>mac</c>), and they travel over XRPC as
/// JSON and inside a CAR as DAG-CBOR.</para>
/// <para>Reading also accepts a bare base64 string, since some services emit one, and base64
/// padding is optional in both directions.</para>
/// </remarks>
public sealed class LexBytesJsonConverter : JsonConverter<byte[]>
{
    /// <inheritdoc/>
    public override byte[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;

            case JsonTokenType.String:
                return DecodeBase64(reader.GetString()!);

            case JsonTokenType.StartObject:
                break;

            default:
                throw new JsonException(
                    $"Expected a Lexicon bytes object or base64 string, got {reader.TokenType}.");
        }

        string? base64 = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return base64 is null
                    ? throw new JsonException("Lexicon bytes object is missing its \"$bytes\" property.")
                    : DecodeBase64(base64);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Malformed Lexicon bytes object.");

            var isBytes = reader.ValueTextEquals("$bytes"u8);
            reader.Read();

            if (isBytes)
            {
                base64 = reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : throw new JsonException("\"$bytes\" must be a base64 string.");
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Unexpected end of JSON while reading a Lexicon bytes object.");
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);

        // Unpadded, which is what the AT Protocol data model specifies and what other
        // implementations emit. Reading accepts either.
        writer.WriteStartObject();
        writer.WriteString("$bytes"u8, Convert.ToBase64String(value).TrimEnd('='));
        writer.WriteEndObject();
    }

    private static byte[] DecodeBase64(string value)
    {
        // The AT Protocol emits unpadded base64; Convert.FromBase64String requires padding.
        var padding = (4 - (value.Length % 4)) % 4;
        return Convert.FromBase64String(padding == 0 ? value : value + new string('=', padding));
    }
}
