using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Reads a boolean from either a JSON boolean or the string a query string carries.
/// </summary>
/// <remarks>
/// The XRPC routing binds a <see cref="bool"/> query parameter from <c>?excludeValues=true</c>
/// without it. It is for a model that is also read from JSON where a boolean may arrive as text,
/// which the serializer, unlike for numbers, has no switch to accept.
/// </remarks>
public sealed class XrpcBooleanConverter : JsonConverter<bool>
{
    /// <inheritdoc/>
    public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            JsonTokenType.String => bool.TryParse(reader.GetString(), out var value)
                ? value
                : throw new JsonException($"Could not read a boolean from '{reader.GetString()}'."),
            _ => throw new JsonException($"Could not read a boolean from a {reader.TokenType} token."),
        };

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteBooleanValue(value);
    }
}
