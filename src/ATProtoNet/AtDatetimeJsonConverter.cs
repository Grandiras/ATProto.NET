using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet;

/// <summary>Reads and writes an <see cref="AtDatetime"/> as a JSON string, exactly as written.</summary>
/// <remarks>Reading never rejects a string token; any other token is a <see cref="JsonException"/>.</remarks>
public sealed class AtDatetimeJsonConverter : JsonConverter<AtDatetime>
{
    /// <inheritdoc />
    public override AtDatetime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException(null, new FormatException($"A datetime must be a JSON string, not {reader.TokenType}."));
        return !reader.ValueIsEscaped && !reader.HasValueSequence && AtDatetime.TryParse(reader.ValueSpan, out var valid) ? valid : AtDatetime.FromWire(reader.GetString()!);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, AtDatetime value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);

    /// <inheritdoc />
    public override AtDatetime ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => AtDatetime.FromWire(reader.GetString()!);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, AtDatetime value, JsonSerializerOptions options) => writer.WritePropertyName(value.Value);
}
