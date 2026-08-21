using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Server.Xrpc;

/// <summary>
/// Reads a boolean from either a JSON boolean or the string a query string carries.
/// </summary>
/// <remarks>
/// Query parameters arrive as text, and the routing binds them by round-tripping through JSON.
/// Numbers survive that because the SDK's serializer options allow reading a number from a
/// string; booleans have no equivalent switch, so a Lexicon parameter declared
/// <c>"type": "boolean"</c> needs this to bind from <c>?excludeValues=true</c>.
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
