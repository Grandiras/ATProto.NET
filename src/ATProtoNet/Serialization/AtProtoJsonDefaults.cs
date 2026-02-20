using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Serialization;

/// <summary>
/// Provides configured JSON serializer options and helpers for AT Protocol data.
/// </summary>
public static class AtProtoJsonDefaults
{
    private static JsonSerializerOptions? _options;

    /// <summary>
    /// Gets the default JSON serializer options configured for AT Protocol data.
    /// </summary>
    public static JsonSerializerOptions Options => _options ??= CreateOptions();

    /// <summary>
    /// Formats a <see cref="DateTime"/> as an AT Protocol-compliant ISO 8601 timestamp
    /// with millisecond precision and UTC "Z" suffix (e.g. "2024-01-15T12:30:45.123Z").
    /// </summary>
    /// <param name="dateTime">The date/time value. Will be treated as UTC.</param>
    /// <returns>An AT Protocol-compliant timestamp string.</returns>
    public static string FormatTimestamp(DateTime dateTime)
        => dateTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    /// <summary>
    /// Gets the current UTC time formatted as an AT Protocol-compliant timestamp.
    /// </summary>
    public static string NowTimestamp() => FormatTimestamp(DateTime.UtcNow);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new UnionJsonConverterFactory());

        return options;
    }
}

/// <summary>
/// JSON converter factory for AT Protocol union types (discriminated by $type field).
/// </summary>
internal sealed class UnionJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeToConvert.IsAssignableTo(typeof(IAtProtoUnion));
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        // For now, return null to let the default serializer handle it
        // The union type resolution will be handled via [JsonDerivedType] attributes
        return null;
    }
}

/// <summary>
/// Marker interface for AT Protocol union types.
/// </summary>
public interface IAtProtoUnion;
