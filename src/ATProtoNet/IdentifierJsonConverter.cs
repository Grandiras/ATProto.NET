using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>Reads and writes an identifier as a JSON string, strictly: invalid text is a <see cref="JsonException"/>.</summary>
/// <typeparam name="T">The identifier type.</typeparam>
/// <remarks>
/// The exception carries the JSON path, with the <see cref="FormatException"/> as its inner exception. Valid text is usually read
/// straight from the UTF-8 bytes. Attached to every identifier type, so source-generated contexts use it without reflection.
/// </remarks>
public sealed class IdentifierJsonConverter<T> : JsonConverter<T> where T : IAtIdentifier<T>
{
    /// <inheritdoc />
    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException(null, new FormatException($"A {typeof(T).Name} must be a JSON string, not {reader.TokenType}."));
        if (!reader.ValueIsEscaped && !reader.HasValueSequence && T.TryParse(reader.ValueSpan, out var fast)) return fast;
        var text = reader.GetString()!;
        return T.TryParse(text, out var result) ? result : throw new JsonException(null, Identifiers.Invalid<T>(text));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);

    /// <inheritdoc />
    public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        T.TryParse(reader.GetString(), out var result) ? result : throw new JsonException(null, Identifiers.Invalid<T>(reader.GetString()!));

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options) => writer.WritePropertyName(value.Value);
}

/// <summary>Reads and writes an <see cref="AtAddress"/> as a JSON string that must parse as one of its three cases.</summary>
public sealed class AtAddressJsonConverter : JsonConverter<AtAddress>
{
    /// <inheritdoc />
    public override AtAddress Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException(null, new FormatException($"An AtAddress must be a JSON string, not {reader.TokenType}."));
        var text = reader.GetString()!;
        return AtAddress.TryParse(text, out var address) ? address : throw new JsonException(null, Identifiers.Invalid<AtAddress>(text));
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AtAddress value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
}
