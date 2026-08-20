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

    private readonly List<string> _warnings = [];

    /// <summary>
    /// Diagnostics collected while emitting — space type declarations that could not be
    /// attributed to an NSID, and definitions that collided with one another.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Loads an assembly and generates Lexicon schema documents for all AT Protocol types found.
    /// </summary>
    /// <param name="assemblyPath">Absolute path to the .NET assembly (.dll).</param>
    /// <returns>A list of (NSID, JSON content) pairs.</returns>
    public List<(string Nsid, string JsonContent)> EmitFromAssembly(string assemblyPath)
        => EmitFromAssembly(Assembly.LoadFrom(assemblyPath));

    /// <summary>
    /// Generates Lexicon schema documents for all AT Protocol types in a loaded assembly.
    /// </summary>
    /// <param name="assembly">The assembly to inspect.</param>
    /// <returns>A list of (NSID, JSON content) pairs.</returns>
    public List<(string Nsid, string JsonContent)> EmitFromAssembly(Assembly assembly)
    {
        var results = new Dictionary<string, LexiconDocument>();

        foreach (var type in assembly.GetExportedTypes())
        {
            // A space type declaration is held by a static class, which is abstract — so this
            // runs before the instance-type filter below.
            EmitSpaceDeclarations(type, results);

            if (type.IsAbstract || type.IsInterface || type.IsEnum)
                continue;

            var typeValue = GetTypeDiscriminator(type);
            if (typeValue is null)
                continue;

            var (nsid, defName) = TypeMapper.ParseTypeValue(typeValue);

            var doc = DocumentFor(results, nsid);
            var schema = BuildSchemaFromType(type, defName);
            AddDefinition(doc, defName, schema, type);
        }

        return results
            .OrderBy(kv => kv.Key)
            .Select(kv => (kv.Key, JsonSerializer.Serialize(kv.Value, s_jsonOptions)))
            .ToList();
    }

    private static LexiconDocument DocumentFor(Dictionary<string, LexiconDocument> results, string nsid)
    {
        if (!results.TryGetValue(nsid, out var doc))
        {
            doc = new LexiconDocument
            {
                Lexicon = 1,
                Id = nsid,
            };
            results[nsid] = doc;
        }

        return doc;
    }

    private void AddDefinition(LexiconDocument doc, string defName, LexiconSchema schema, Type source)
    {
        if (doc.Defs.TryGetValue(defName, out var existing))
        {
            _warnings.Add(
                $"{doc.Id}#{defName}: '{source.FullName}' declares a '{schema.Type}' definition where a " +
                $"'{existing.Type}' one was already emitted — the first one is kept");
            return;
        }

        doc.Defs[defName] = schema;
    }

    /// <summary>
    /// The SDK type a space type declaration is an instance of. Matched by name because the
    /// generator does not reference the SDK — the assembly under inspection carries its own.
    /// </summary>
    private const string SpaceTypeDeclarationName = "ATProtoNet.Spaces.SpaceTypeDeclaration";

    /// <summary>
    /// Emits a <c>"type": "space"</c> definition for every <c>SpaceTypeDeclaration</c> a type
    /// exposes as a static member.
    /// </summary>
    /// <remarks>
    /// A space type has no record shape to reflect over, so it is not discovered the way a
    /// record is. The declaration itself holds the name and collection set; the NSID it grants
    /// on comes from a sibling <c>Nsid</c> (or <c>SpaceType</c>) string constant on the same
    /// type — which is what <c>atproto-lexgen csharp</c> generates for a space Lexicon.
    /// </remarks>
    private void EmitSpaceDeclarations(Type type, Dictionary<string, LexiconDocument> results)
    {
        foreach (var (memberName, declaration) in GetStaticSpaceDeclarations(type))
        {
            var nsid = GetDeclaredNsid(type);
            if (nsid is null)
            {
                _warnings.Add(
                    $"{type.FullName}.{memberName}: space type declaration has no NSID — add a " +
                    "'public const string Nsid' (or 'SpaceType') naming the space type it declares");
                continue;
            }

            AddDefinition(DocumentFor(results, nsid), "main", BuildSpaceSchema(declaration), type);
        }
    }

    /// <summary>Reads every public static <c>SpaceTypeDeclaration</c> a type exposes.</summary>
    private static IEnumerable<(string MemberName, object Declaration)> GetStaticSpaceDeclarations(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        List<(string Name, Type MemberType, Func<object?> Read)> members = [];

        foreach (var property in type.GetProperties(flags))
        {
            if (property.CanRead && property.GetIndexParameters().Length == 0)
                members.Add((property.Name, property.PropertyType, () => property.GetValue(null)));
        }

        foreach (var field in type.GetFields(flags))
            members.Add((field.Name, field.FieldType, () => field.GetValue(null)));

        foreach (var (name, memberType, read) in members)
        {
            if (memberType.FullName != SpaceTypeDeclarationName)
                continue;

            object? declaration;
            try
            {
                declaration = read();
            }
            catch
            {
                // A static initializer that throws is not something to fail the whole run over.
                continue;
            }

            if (declaration is not null)
                yield return (name, declaration);
        }
    }

    /// <summary>The space type NSID a declaration holder names, from its own constants.</summary>
    private static string? GetDeclaredNsid(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;

        foreach (var candidate in new[] { "Nsid", "SpaceType" })
        {
            var field = type.GetField(candidate, flags);
            if (field?.FieldType == typeof(string) && field.GetValue(null) is string fromField && fromField.Contains('.'))
                return fromField;

            var property = type.GetProperty(candidate, flags);
            if (property?.PropertyType == typeof(string) && property.GetValue(null) is string fromProperty && fromProperty.Contains('.'))
                return fromProperty;
        }

        return null;
    }

    /// <summary>Maps a <c>SpaceTypeDeclaration</c> instance back to its Lexicon definition.</summary>
    private static LexiconSchema BuildSpaceSchema(object declaration)
    {
        var type = declaration.GetType();

        string? Read(string name) => type.GetProperty(name)?.GetValue(declaration) as string;

        var collections = (type.GetProperty("Collections")?.GetValue(declaration) as IEnumerable<string>)?.ToList();
        var localized = type.GetProperty("LocalizedNames")?.GetValue(declaration) as IEnumerable<KeyValuePair<string, string>>;

        return new LexiconSchema
        {
            Type = "space",
            Description = Read("Description"),
            Key = Read("Key"),
            Name = Read("Name"),
            LocalizedNames = localized?.ToDictionary(kv => kv.Key, kv => kv.Value) is { Count: > 0 } names ? names : null,
            Collections = collections ?? [],
        };
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
