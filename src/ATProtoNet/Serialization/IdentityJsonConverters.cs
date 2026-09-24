using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;

namespace ATProtoNet.Serialization;

/// <summary>
/// Reads and writes an identifier type as its JSON string form.
/// </summary>
/// <remarks>
/// An invalid value fails as a <see cref="JsonException"/>, which the serializer completes with
/// the JSON path and position of the offending string; the <see cref="FormatException"/> it
/// wraps names the value and the expected type.
/// </remarks>
/// <typeparam name="T">The identifier type.</typeparam>
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
