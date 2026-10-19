using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Data;

/// <summary>Reads and writes a <see cref="DecimalString"/> as a JSON string, exactly as written.</summary>
/// <remarks>Reading never rejects a string token; a number or any other token is a <see cref="JsonException"/>.</remarks>
public sealed class DecimalStringJsonConverter : JsonConverter<DecimalString>
{
    /// <inheritdoc />
    public override DecimalString Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException(null, new FormatException($"A decimal string must be a JSON string, not {reader.TokenType}."));
        return !reader.ValueIsEscaped && !reader.HasValueSequence && DecimalString.TryParse(reader.ValueSpan, out var valid) ? valid : DecimalString.FromWire(reader.GetString()!);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, DecimalString value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);

    /// <inheritdoc />
    public override DecimalString ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) => DecimalString.FromWire(reader.GetString()!);

    /// <inheritdoc />
    public override void WriteAsPropertyName(Utf8JsonWriter writer, DecimalString value, JsonSerializerOptions options) => writer.WritePropertyName(value.Value);
}
