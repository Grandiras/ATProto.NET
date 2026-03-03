using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.CodeGen;

/// <summary>
/// Generates Lexicon JSON schema documents from compiled .NET assemblies.
/// Inspects types via reflection, looking for <c>[JsonPropertyName("$type")]</c> properties
/// to identify AT Protocol record types, then maps their properties back to Lexicon schemas.
/// </summary>
public sealed class LexiconEmitter
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Loads an assembly and generates Lexicon schema documents for all AT Protocol types found.
    /// </summary>
    /// <param name="assemblyPath">Absolute path to the .NET assembly (.dll).</param>
    /// <returns>A list of (NSID, JSON content) pairs.</returns>
    public List<(string Nsid, string JsonContent)> EmitFromAssembly(string assemblyPath)
    {
        var assembly = Assembly.LoadFrom(assemblyPath);
        var results = new Dictionary<string, LexiconDocument>();

        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface || type.IsEnum)
                continue;

            var typeValue = GetTypeDiscriminator(type);
            if (typeValue is null)
                continue;

            var (nsid, defName) = TypeMapper.ParseTypeValue(typeValue);

            if (!results.TryGetValue(nsid, out var doc))
            {
                doc = new LexiconDocument
                {
                    Lexicon = 1,
                    Id = nsid,
                };
                results[nsid] = doc;
            }

            var schema = BuildSchemaFromType(type, defName);
            doc.Defs[defName] = schema;
        }

        return results
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, JsonSerializer.Serialize(kv.Value, s_jsonOptions)))
            .ToList();
    }

    /// <summary>
    /// Gets the <c>$type</c> discriminator value from a type, if it has one.
    /// Looks for a property annotated with <c>[JsonPropertyName("$type")]</c>.
    /// </summary>
    private static string? GetTypeDiscriminator(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var prop in properties)
        {
            var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonAttr?.Name != "$type")
                continue;

            if (prop.PropertyType != typeof(string))
                continue;

            // Try to get the value from an uninitialized instance
            // (works for expression-body properties like `public string Type => "nsid"`)
            try
            {
                var instance = RuntimeHelpers.GetUninitializedObject(type);
                var value = prop.GetValue(instance) as string;
                if (!string.IsNullOrEmpty(value))
                    return value;
            }
            catch
            {
                // If we can't create an instance, skip this type
            }

            break;
        }

        return null;
    }

    /// <summary>
    /// Builds a Lexicon schema definition from a C# type's properties.
    /// </summary>
    private static LexiconSchema BuildSchemaFromType(Type type, string defName)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var schemaProperties = new Dictionary<string, LexiconSchema>();
        var required = new List<string>();

        foreach (var prop in properties)
        {
            var jsonAttr = prop.GetCustomAttribute<JsonPropertyNameAttribute>();
            if (jsonAttr is null)
                continue;

            var jsonName = jsonAttr.Name;

            // Skip the $type property — it's the discriminator, not a data property
            if (jsonName == "$type")
                continue;

            var propSchema = MapPropertyToSchema(prop);
            schemaProperties[jsonName] = propSchema;

            // Detect required properties
            if (IsRequired(prop))
                required.Add(jsonName);
        }

        // Record types wrap properties in a record → object schema
        var objectSchema = new LexiconSchema
        {
            Type = "object",
            Required = required.Count > 0 ? required : null,
            Properties = schemaProperties.Count > 0 ? schemaProperties : null,
        };

        return new LexiconSchema
        {
            Type = "record",
            Key = "tid",
            Record = objectSchema,
        };
    }

    /// <summary>
    /// Maps a single C# property to a Lexicon property schema.
    /// </summary>
    private static LexiconSchema MapPropertyToSchema(PropertyInfo prop)
    {
        var propType = prop.PropertyType;

        // Unwrap Nullable<T>
        var underlyingType = System.Nullable.GetUnderlyingType(propType);
        if (underlyingType is not null)
            propType = underlyingType;

        // List<T> → array
        if (propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>))
        {
            var itemType = propType.GetGenericArguments()[0];
            return new LexiconSchema
            {
                Type = "array",
                Items = MapClrTypeToSchema(itemType),
            };
        }

        return MapClrTypeToSchema(propType);
    }

    /// <summary>
    /// Maps a CLR type to a Lexicon schema type.
    /// </summary>
    private static LexiconSchema MapClrTypeToSchema(Type type)
    {
        // Basic scalar types
        if (type == typeof(string))
            return new LexiconSchema { Type = "string" };

        if (type == typeof(long) || type == typeof(int))
            return new LexiconSchema { Type = "integer" };

        if (type == typeof(bool))
            return new LexiconSchema { Type = "boolean" };

        if (type == typeof(byte[]))
            return new LexiconSchema { Type = "bytes" };

        if (type == typeof(JsonElement))
            return new LexiconSchema { Type = "unknown" };

        // DateTime → string with format
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return new LexiconSchema { Type = "string", Format = "datetime" };

        // For complex types, emit a ref to their $type if they have one,
        // otherwise use "unknown"
        var typeValue = GetTypeDiscriminator(type);
        if (typeValue is not null)
            return new LexiconSchema { Type = "ref", Ref = typeValue };

        // Check if it's a known type from the assembly
        var fullName = type.FullName ?? type.Name;
        if (fullName.Contains("BlobRef", StringComparison.OrdinalIgnoreCase))
            return new LexiconSchema { Type = "blob" };

        return new LexiconSchema { Type = "unknown" };
    }

    /// <summary>
    /// Detects whether a property is required (uses the C# <c>required</c> modifier).
    /// </summary>
    private static bool IsRequired(PropertyInfo prop)
    {
        // In C# 11+, the 'required' modifier adds RequiredMemberAttribute to the type
        // and SetsRequiredMembersAttribute to constructors. We check for the attribute
        // on the property itself via metadata.
        var declaringType = prop.DeclaringType;
        if (declaringType is null)
            return false;

        // Check if the type has RequiredMemberAttribute (indicates it has required members)
        var hasRequiredMembers = declaringType
            .GetCustomAttributes()
            .Any(a => a.GetType().Name == "RequiredMemberAttribute");

        if (!hasRequiredMembers)
            return false;

        // Check if this specific property is marked required
        // Required properties have the RequiredAttribute from System.Runtime.CompilerServices
        return prop.GetCustomAttributes()
            .Any(a => a.GetType().Name == "RequiredMemberAttribute"
                    || a.GetType().FullName == "System.Runtime.CompilerServices.RequiredMemberAttribute");
    }

    /// <summary>
    /// Gets the JSON serializer options used for emitting Lexicon JSON.
    /// </summary>
    public static JsonSerializerOptions JsonOptions => s_jsonOptions;
}
