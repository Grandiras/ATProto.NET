using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Serialization;

/// <summary>
/// Provides configured JSON serializer options for AT Protocol data.
/// </summary>
public static class AtProtoJsonDefaults
{
    private static JsonSerializerOptions? _options;

    /// <summary>
    /// Gets the default JSON serializer options configured for AT Protocol data.
    /// </summary>
    public static JsonSerializerOptions Options => _options ??= CreateOptions();

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
