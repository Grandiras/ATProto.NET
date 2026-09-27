using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;

namespace ATProtoNet.Serialization;

// Reads and writes an identifier type as its JSON string form.
//
// An invalid value fails as a JsonException, which the serializer completes with the JSON path and
// position of the offending string; the FormatException it wraps names the value and the expected type.
//
// T: The identifier type.
internal sealed class IdentifierJsonConverter<T> : JsonConverter<T>
    where T : class, IIdentifier<T>
{
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (value is null)
            return null;

        if (T.TryCreate(value, value, out var result))
            return result;

        // A null message makes the serializer supply its own, with the path appended.
        throw new JsonException(null, new FormatException($"'{value}' is not a valid {typeof(T).Name}."));
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}

// Reads and writes an AtDatetime as its JSON string, exactly as written.
//
// Reading never rejects a string, so one malformed timestamp does not fail a whole response; the value
// keeps the text and reports AtDatetime.IsValid false. A token that is not a string is an error.
internal sealed class AtDatetimeJsonConverter : JsonConverter<AtDatetime>
{
    public override AtDatetime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException(null, new FormatException($"A datetime must be a JSON string, not {reader.TokenType}."));

        return AtDatetime.FromWire(reader.GetString()!);
    }

    public override void Write(Utf8JsonWriter writer, AtDatetime value, JsonSerializerOptions options)
    {
        if (!value.HasText)
        {
            throw new InvalidOperationException(
                "An uninitialized (default) AtDatetime cannot be serialized. Set it with " +
                "AtDatetime.Now(), AtDatetime.Parse or AtDatetime.FromDateTimeOffset.");
        }

        writer.WriteStringValue(value.ToString());
    }
}
