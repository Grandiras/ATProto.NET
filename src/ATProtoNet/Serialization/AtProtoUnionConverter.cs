using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Serialization;

// The shape of one AtProtoUnionAttribute base: its declared variants, its unknown variant, and whether
// it is closed. Built once per base type by reflection.
internal sealed class AtProtoUnionShape
{
    private static readonly ConcurrentDictionary<Type, AtProtoUnionShape?> Shapes = new();

    private readonly ConstructorInfo? _unknownConstructor;

    private AtProtoUnionShape(
        Type baseType,
        AtProtoUnionAttribute attribute,
        Dictionary<string, Type> variants,
        Dictionary<Type, string> discriminators,
        ConstructorInfo? unknownConstructor)
    {
        BaseType = baseType;
        Closed = attribute.Closed;
        UnknownVariant = attribute.UnknownVariant;
        Variants = variants;
        Discriminators = discriminators;
        _unknownConstructor = unknownConstructor;
    }

    public Type BaseType { get; }

    public bool Closed { get; }

    public Type? UnknownVariant { get; }

    // Variants declared with JsonDerivedTypeAttribute, by discriminator.
    public IReadOnlyDictionary<string, Type> Variants { get; }

    // The inverse of Variants.
    public IReadOnlyDictionary<Type, string> Discriminators { get; }

    // Returns the shape of type, or null when it is not a union base.
    //
    // Throws InvalidOperationException: The type is marked but declared inconsistently.
    public static AtProtoUnionShape? Get(Type type) => Shapes.GetOrAdd(type, Create);

    public object CreateUnknown(string type, JsonElement raw) => _unknownConstructor!.Invoke([type, raw]);

    // Walks the base classes of variant for the union base it belongs to.
    public static AtProtoUnionShape? FindOwner(Type variant)
    {
        for (var type = variant.BaseType; type is not null && type != typeof(object); type = type.BaseType)
        {
            if (Get(type) is { } shape)
                return shape;
        }

        return null;
    }

    private static AtProtoUnionShape? Create(Type type)
    {
        var attribute = type.GetCustomAttribute<AtProtoUnionAttribute>(inherit: false);
        if (attribute is null)
            return null;

        if (!type.IsClass || !type.IsAbstract)
            throw Invalid(type, "must be an abstract class");

        ConstructorInfo? unknownConstructor = null;
        if (attribute.Closed)
        {
            if (attribute.UnknownVariant is not null)
                throw Invalid(type, "is closed, so it cannot also name an unknown variant");
        }
        else
        {
            var unknown = attribute.UnknownVariant
                ?? throw Invalid(type, "is open, so it must name its unknown variant: "
                    + "[AtProtoUnion(typeof(Unknown…))], or [AtProtoUnion(Closed = true)] if the Lexicon marks it closed");

            if (unknown.IsAbstract || !type.IsAssignableFrom(unknown) || !typeof(IUnknownUnionVariant).IsAssignableFrom(unknown))
                throw Invalid(type, $"names '{unknown.Name}' as its unknown variant, which must be a concrete subclass implementing {nameof(IUnknownUnionVariant)}");

            unknownConstructor = unknown.GetConstructor([typeof(string), typeof(JsonElement)])
                ?? throw Invalid(type, $"names '{unknown.Name}' as its unknown variant, which needs a public (string type, JsonElement raw) constructor");
        }

        var variants = new Dictionary<string, Type>(StringComparer.Ordinal);
        var discriminators = new Dictionary<Type, string>();
        foreach (var derived in type.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false))
        {
            if (derived.TypeDiscriminator is not string discriminator || discriminator.Length == 0)
                throw Invalid(type, $"declares '{derived.DerivedType.Name}' without a string $type discriminator");

            if (!variants.TryAdd(discriminator, derived.DerivedType))
                throw Invalid(type, $"declares the discriminator '{discriminator}' twice");

            discriminators.TryAdd(derived.DerivedType, discriminator);
        }

        return new AtProtoUnionShape(type, attribute, variants, discriminators, unknownConstructor);
    }

    private static InvalidOperationException Invalid(Type type, string problem)
        => new($"The Lexicon union base '{type.FullName}' {problem}.");
}

// Supplies the converters for AtProtoUnionAttribute bases and their unknown variants.
internal sealed class AtProtoUnionConverterFactory(LexiconTypeRegistry registry) : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
        => AtProtoUnionShape.Get(typeToConvert) is not null
            || (typeof(IUnknownUnionVariant).IsAssignableFrom(typeToConvert)
                && AtProtoUnionShape.FindOwner(typeToConvert)?.UnknownVariant == typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (AtProtoUnionShape.Get(typeToConvert) is { } shape)
        {
            return (JsonConverter)Activator.CreateInstance(
                typeof(AtProtoUnionConverter<>).MakeGenericType(typeToConvert), shape, registry)!;
        }

        return (JsonConverter)Activator.CreateInstance(
            typeof(UnknownUnionVariantConverter<>).MakeGenericType(typeToConvert))!;
    }
}

// Reads a union by its $type, wherever it sits in the object, and writes a variant through its own
// contract, which leads with $type.
//
// Resolution order: the variants declared on the base, then the registry (consulted on every miss, so a
// registration takes effect even after this converter was built), then the unknown variant. A closed
// union resolves its declared variants only — the registry refuses variants for one — and anything else
// is an error.
internal sealed class AtProtoUnionConverter<TBase>(AtProtoUnionShape shape, LexiconTypeRegistry registry)
    : JsonConverter<TBase>
    where TBase : class
{
    public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected a JSON object for the Lexicon union '{typeof(TBase).Name}', got {reader.TokenType}.");

        var discriminator = ReadDiscriminator(reader)
            ?? throw new JsonException($"A '{typeof(TBase).Name}' union member has no \"$type\".");

        if (shape.Variants.TryGetValue(discriminator, out var variant)
            || (!shape.Closed && registry.TryGetVariant(typeof(TBase), discriminator, out variant)))
        {
            return (TBase?)JsonSerializer.Deserialize(ref reader, options.GetTypeInfo(variant));
        }

        if (shape.Closed)
            throw new JsonException($"'{discriminator}' is not a variant of the closed Lexicon union '{typeof(TBase).Name}'.");

        return (TBase)shape.CreateUnknown(discriminator, JsonElement.ParseValue(ref reader));
    }

    public override void Write(Utf8JsonWriter writer, TBase value, JsonSerializerOptions options)
    {
        if (value is IUnknownUnionVariant unknown)
        {
            UnknownUnionVariant.WriteRaw(writer, unknown);
            return;
        }

        var variant = value.GetType();
        if (!shape.Discriminators.ContainsKey(variant) && !registry.TryGetDiscriminator(typeof(TBase), variant, out _))
        {
            throw new NotSupportedException(
                $"'{variant.Name}' is not a declared or registered variant of the Lexicon union '{typeof(TBase).Name}', "
                + $"so it has no $type to write. Register it with {nameof(LexiconTypeRegistry)}.{nameof(LexiconTypeRegistry.RegisterUnionVariant)}.");
        }

        JsonSerializer.Serialize(writer, value, options.GetTypeInfo(variant));
    }

    // Finds the object's top-level $type on a copy of the reader. The serializer buffers a whole value
    // before handing it to a converter that cannot resume, so the scan never runs out of data. The
    // appview often puts $type last.
    private static string? ReadDiscriminator(Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isType = reader.ValueTextEquals("$type"u8);
            reader.Read();

            if (isType)
            {
                return reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : throw new JsonException("A union member's \"$type\" must be a string.");
            }

            if (!reader.TrySkip())
                throw new JsonException("Incomplete JSON while looking for a union member's \"$type\".");
        }

        return null;
    }
}

// Reads and writes an IUnknownUnionVariant that is not declared as its union base.
internal sealed class UnknownUnionVariantConverter<TUnknown> : JsonConverter<TUnknown>
    where TUnknown : class, IUnknownUnionVariant
{
    public override TUnknown Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = JsonElement.ParseValue(ref reader);
        if (raw.ValueKind != JsonValueKind.Object
            || !raw.TryGetProperty("$type"u8, out var type)
            || type.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"'{typeof(TUnknown).Name}' must be read from a JSON object with a string \"$type\".");
        }

        return (TUnknown)AtProtoUnionShape.FindOwner(typeof(TUnknown))!.CreateUnknown(type.GetString()!, raw);
    }

    public override void Write(Utf8JsonWriter writer, TUnknown value, JsonSerializerOptions options)
        => UnknownUnionVariant.WriteRaw(writer, value);
}

// Helpers shared by the SDK's Unknown* union variants.
internal static class UnknownUnionVariant
{
    // Validates the raw object of an unknown variant and detaches it from any JsonDocument the caller
    // may dispose. Elements the serializer produced are already detached, so for them this copies
    // nothing.
    public static JsonElement RequireObject(JsonElement raw, [CallerArgumentExpression(nameof(raw))] string? paramName = null)
    {
        if (raw.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("An unknown union variant holds a JSON object.", paramName);

        return raw.Clone();
    }

    // Validates the $type discriminator of an unknown variant.
    public static string RequireType(string type, [CallerArgumentExpression(nameof(type))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(type, paramName);
        return type;
    }

    // Writes the variant's original bytes, so escapes and number formatting survive unchanged.
    public static void WriteRaw(Utf8JsonWriter writer, IUnknownUnionVariant variant)
        => writer.WriteRawValue(JsonMarshal.GetRawUtf8Value(variant.Raw), skipInputValidation: true);
}
