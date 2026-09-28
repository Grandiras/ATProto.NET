using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ATProtoNet.Serialization;

// The shape of one AtProtoUnionAttribute base: its declared variants, its unknown variant, and whether
// it is closed. Built once per base type by reflection.
internal sealed class AtProtoUnionShape
{
    private static readonly ConcurrentDictionary<Type, AtProtoUnionShape?> Shapes = new();

    private readonly ConstructorInfo? _unknownConstructor;

    // The declared variants' positions in DeclaredVariants, by the UTF-8 bytes of their discriminator, so
    // that a $type is matched without decoding it.
    private readonly Dictionary<byte[], int>.AlternateLookup<ReadOnlySpan<byte>> _utf8Variants;

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
        DeclaredVariants = [.. variants.Values];
        _utf8Variants = variants.Keys
            .Select((discriminator, index) => (Key: Encoding.UTF8.GetBytes(discriminator), Index: index))
            .ToDictionary(v => v.Key, v => v.Index, Utf8KeyComparer.Instance)
            .GetAlternateLookup<ReadOnlySpan<byte>>();
    }

    public Type BaseType { get; }

    public bool Closed { get; }

    public Type? UnknownVariant { get; }

    // Variants declared with JsonDerivedTypeAttribute, by discriminator.
    public IReadOnlyDictionary<string, Type> Variants { get; }

    // The inverse of Variants.
    public IReadOnlyDictionary<Type, string> Discriminators { get; }

    // The values of Variants, in a fixed order.
    public Type[] DeclaredVariants { get; }

    // Returns the shape of type, or null when it is not a union base.
    //
    // Throws InvalidOperationException: The type is marked but declared inconsistently.
    public static AtProtoUnionShape? Get(Type type) => Shapes.GetOrAdd(type, Create);

    public object CreateUnknown(string type, JsonElement raw) => _unknownConstructor!.Invoke([type, raw]);

    // Finds a declared variant, as its position in DeclaredVariants, by the unescaped UTF-8 bytes of its
    // discriminator.
    public bool TryGetVariant(ReadOnlySpan<byte> discriminator, out int index) =>
        _utf8Variants.TryGetValue(discriminator, out index);

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

    // Compares UTF-8 keys by content, and takes a span as an alternate key so lookups need not copy.
    private sealed class Utf8KeyComparer
        : IEqualityComparer<byte[]>, IAlternateEqualityComparer<ReadOnlySpan<byte>, byte[]>
    {
        public static readonly Utf8KeyComparer Instance = new();

        public bool Equals(byte[]? x, byte[]? y) => x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => GetHashCode((ReadOnlySpan<byte>)obj);

        public bool Equals(ReadOnlySpan<byte> alternate, byte[] other) => alternate.SequenceEqual(other);

        public int GetHashCode(ReadOnlySpan<byte> alternate)
        {
            var hash = new HashCode();
            hash.AddBytes(alternate);
            return hash.ToHashCode();
        }

        public byte[] Create(ReadOnlySpan<byte> alternate) => alternate.ToArray();
    }
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
    // The reader of each variant read so far: the declared ones by their position in
    // AtProtoUnionShape.DeclaredVariants, the registered ones by type. A factory's converter serves the one
    // options instance it was created for, but each reader still checks, rather than rely on that.
    private readonly VariantReader?[] _declaredReaders = new VariantReader?[shape.DeclaredVariants.Length];
    private readonly ConcurrentDictionary<Type, VariantReader> _registeredReaders = new();

    public override TBase? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException($"Expected a JSON object for the Lexicon union '{typeof(TBase).Name}', got {reader.TokenType}.");

        var discriminator = reader;
        if (!FindDiscriminator(ref discriminator))
            throw new JsonException($"A '{typeof(TBase).Name}' union member has no \"$type\".");

        // A declared variant is matched on the raw bytes; the string is only needed past that.
        if (!discriminator.ValueIsEscaped && !discriminator.HasValueSequence
            && shape.TryGetVariant(discriminator.ValueSpan, out var index))
        {
            var declared = _declaredReaders[index];
            if (declared is null || declared.Options != options)
                declared = _declaredReaders[index] = VariantReader.Create(shape.DeclaredVariants[index], options);

            return declared.Read(ref reader);
        }

        var type = discriminator.GetString()!;
        if (!shape.Variants.TryGetValue(type, out var variant)
            && (shape.Closed || !registry.TryGetVariant(typeof(TBase), type, out variant)))
        {
            if (shape.Closed)
                throw new JsonException($"'{type}' is not a variant of the closed Lexicon union '{typeof(TBase).Name}'.");

            return (TBase)shape.CreateUnknown(type, JsonElement.ParseValue(ref reader));
        }

        if (!_registeredReaders.TryGetValue(variant, out var variantReader) || variantReader.Options != options)
            variantReader = _registeredReaders[variant] = VariantReader.Create(variant, options);

        return variantReader.Read(ref reader);
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

    // Moves a copy of the reader to the value of the object's top-level $type, a string, and returns
    // false when there is none. The serializer buffers a whole value before handing it to a converter
    // that cannot resume, so the scan never runs out of data. The appview often puts $type last.
    private static bool FindDiscriminator(ref Utf8JsonReader reader)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            var isType = reader.ValueTextEquals("$type"u8);
            reader.Read();

            if (isType)
            {
                return reader.TokenType == JsonTokenType.String
                    ? true
                    : throw new JsonException("A union member's \"$type\" must be a string.");
            }

            if (!reader.TrySkip())
                throw new JsonException("Incomplete JSON while looking for a union member's \"$type\".");
        }

        return false;
    }

    // Reads one variant with its own converter, on the caller's reader. JsonSerializer.Deserialize would
    // first skip the whole value to scope a new reader to it: a second pass over every union member, and
    // one more for each union nested in it.
    //
    // A failure is handled by the outermost union being read directly. A union nested in it records its
    // failure and passes it on, and a converter in between may even catch it. When the member failed,
    // or anything in it did, the outermost union reads the member again with JsonSerializer.Deserialize,
    // as do the unions nested in it, from where the member starts. That read's value or error, and where
    // it leaves the reader, are those of a read that never took the direct path. A failed member is thus
    // read twice however deeply its unions nest, the second time after the first read has unwound.
    private abstract class VariantReader(JsonSerializerOptions options)
    {
        public JsonSerializerOptions Options { get; } = options;

        public static VariantReader Create(Type variant, JsonSerializerOptions options) =>
            (VariantReader)Activator.CreateInstance(
                typeof(VariantReader<>).MakeGenericType(typeof(TBase), variant), options.GetTypeInfo(variant))!;

        public abstract TBase? Read(ref Utf8JsonReader reader);
    }

    private sealed class VariantReader<TVariant>(JsonTypeInfo typeInfo) : VariantReader(typeInfo.Options)
    {
        // Only the serializer's own converters are called directly, as they read exactly the one value.
        // JsonSerializer.Deserialize confines a custom converter to the value and checks that it read all
        // of it.
        private readonly JsonConverter<TVariant>? _converter =
            typeInfo.Converter.GetType().Assembly == typeof(JsonSerializer).Assembly
                ? typeInfo.Converter as JsonConverter<TVariant>
                : null;

        // typeof(TVariant) costs a lookup in code shared across reference types.
        private readonly Type _type = typeof(TVariant);

        public override TBase? Read(ref Utf8JsonReader reader)
        {
            var mode = UnionReadState.Mode;
            if (_converter is null || mode == UnionReadMode.Reread)
                return (TBase?)JsonSerializer.Deserialize(ref reader, typeInfo);

            if (mode != UnionReadMode.None)
            {
                try
                {
                    return (TBase?)(object?)_converter.Read(ref reader, _type, Options);
                }
                catch (Exception) when (UnionReadState.RecordFailure())
                {
                    // Never entered: the filter records the failure and lets it pass.
                    throw;
                }
            }

            var start = reader;
            UnionReadState.Mode = UnionReadMode.Direct;
            try
            {
                var value = (TBase?)(object?)_converter.Read(ref reader, _type, Options);
                if (UnionReadState.Mode == UnionReadMode.Direct)
                    return value;
            }
            catch (Exception)
            {
                // Read again below, once the failed read has unwound.
            }
            finally
            {
                UnionReadState.Mode = UnionReadMode.None;
            }

            reader = start;
            UnionReadState.Mode = UnionReadMode.Reread;
            try
            {
                return (TBase?)JsonSerializer.Deserialize(ref reader, typeInfo);
            }
            finally
            {
                UnionReadState.Mode = UnionReadMode.None;
            }
        }
    }
}

// How the union converters on this thread are reading a member. A converter runs synchronously, so a
// read starts and ends on the thread that set the mode.
internal static class UnionReadState
{
    [ThreadStatic]
    public static UnionReadMode Mode;

    // Records that a union nested in a direct read failed, for an exception filter; returns false.
    public static bool RecordFailure()
    {
        Mode = UnionReadMode.DirectFailed;
        return false;
    }
}

internal enum UnionReadMode
{
    // No union member is being read.
    None,

    // The outermost union reads its member directly, and handles a failure of the unions nested in it.
    Direct,

    // As Direct, after a union nested in the member failed; the member is read again even if a converter
    // caught that failure.
    DirectFailed,

    // A member is read again with JsonSerializer.Deserialize, which the unions nested in it use too.
    Reread,
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
