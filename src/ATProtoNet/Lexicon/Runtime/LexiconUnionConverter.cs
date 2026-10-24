using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace ATProtoNet.Lexicon.Runtime;

/// <summary>
/// The base of every generated union converter (LEX-51, LEX-54): it finds <c>$type</c> among the object's properties in any
/// order, dispatches to the matching case's contract, and writes the active case with <c>$type</c> first. A generated subclass
/// supplies only its case table.
/// </summary>
/// <typeparam name="TUnion">The generated union.</typeparam>
/// <remarks>
/// An unlisted <c>$type</c> reads as <see cref="LexiconUnknown"/> in an open union and fails in a closed one. JSON <c>null</c> reads
/// as the default union (no active case); writing one fails, because an optional union is omitted before it reaches the
/// converter (LEX-84).
/// </remarks>
public abstract class LexiconUnionConverter<TUnion> : JsonConverter<TUnion> where TUnion : struct, IUnion
{
    private readonly Dictionary<string, UnionCase> _byType;
    private readonly Dictionary<Type, UnionCase> _byClrType;
    private readonly Func<LexiconUnknown, TUnion>? _unknown;

    /// <summary>Creates the converter.</summary>
    /// <param name="unknown">Wraps an unlisted member in an open union, such as <c>u =&gt; new PostEmbed(u)</c>; <see langword="null"/> for a closed union.</param>
    /// <param name="cases">The cases, from <see cref="Case{T}"/>.</param>
    protected LexiconUnionConverter(Func<LexiconUnknown, TUnion>? unknown, params ReadOnlySpan<UnionCase> cases)
    {
        _unknown = unknown;
        _byType = new(cases.Length, StringComparer.Ordinal);
        _byClrType = new(cases.Length);
        foreach (var unionCase in cases)
        {
            _byType.Add(unionCase.LexiconType, unionCase);
            _byClrType.Add(unionCase.ClrType, unionCase);
        }
    }

    /// <summary>JSON <c>null</c> reaches <see cref="Read"/>, which reads it as the default union.</summary>
    public override bool HandleNull => true;

    /// <summary>A case of the union: a generated type and how the union wraps it.</summary>
    /// <typeparam name="T">The case type.</typeparam>
    /// <param name="wrap">Creates the union from a value of the case, such as <c>v =&gt; new PostEmbed(v)</c>.</param>
    /// <returns>The case.</returns>
    protected static UnionCase Case<T>(Func<T, TUnion> wrap) where T : class, ILexiconType<T> =>
        new(T.LexiconType, typeof(T), element => wrap(element.Deserialize(T.JsonTypeInfo) ?? throw new JsonException("A union member is not null.")),
            value => LexiconJson.SerializeWithType((T)value, T.JsonTypeInfo, T.LexiconType));

    /// <inheritdoc />
    public override TUnion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return default;
        if (reader.TokenType != JsonTokenType.StartObject) throw new JsonException($"A union member is an object, not {reader.TokenType}.");
        var element = JsonElement.ParseValue(ref reader);
        if (!element.TryGetProperty("$type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String || typeElement.GetString() is not { Length: > 0 } type)
            throw new JsonException("A union member has a non-empty string $type.");
        if (_byType.TryGetValue(type, out var unionCase)) return unionCase.Read(element);
        return _unknown is not null ? _unknown(new LexiconUnknown(element)) : throw new JsonException($"\"{type}\" is not a member of the closed union {typeof(TUnion).Name}.");
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TUnion value, JsonSerializerOptions options)
    {
        switch (value.Value)
        {
            case null:
                throw new JsonException($"The {typeof(TUnion).Name} has no active case; a required union must be set.");
            case LexiconUnknown unknown:
                unknown.Raw.WriteTo(writer);
                return;
            case var active when _byClrType.TryGetValue(active.GetType(), out var unionCase):
                writer.WriteRawValue(unionCase.Write(active), skipInputValidation: true);
                return;
            default:
                throw new JsonException($"{value.Value.GetType().Name} is not a case of {typeof(TUnion).Name}.");
        }
    }

    /// <summary>One case of a generated union.</summary>
    protected sealed class UnionCase
    {
        internal UnionCase(string lexiconType, Type clrType, Func<JsonElement, TUnion> read, Func<object, byte[]> write)
        {
            LexiconType = lexiconType;
            ClrType = clrType;
            Read = read;
            Write = write;
        }

        internal string LexiconType { get; }
        internal Type ClrType { get; }
        internal Func<JsonElement, TUnion> Read { get; }
        internal Func<object, byte[]> Write { get; }
    }
}

/// <summary>Reads a union property the Lexicon requires: a missing value or JSON <c>null</c> is a <see cref="JsonException"/> (LEX-84).</summary>
/// <typeparam name="TUnion">The generated union.</typeparam>
public sealed class LexiconRequiredUnionConverter<TUnion> : JsonConverter<TUnion> where TUnion : struct, IUnion
{
    /// <summary>JSON <c>null</c> reaches <see cref="Read"/>, which refuses it.</summary>
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override TUnion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null
            ? throw new JsonException($"A required {typeof(TUnion).Name} cannot be null.")
            : Union(options).Read(ref reader, typeToConvert, options);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TUnion value, JsonSerializerOptions options) => Union(options).Write(writer, value, options);

    // The union's own converter, called directly so errors keep the outer JSON path.
    private static JsonConverter<TUnion> Union(JsonSerializerOptions options) => (JsonConverter<TUnion>)options.GetTypeInfo(typeof(TUnion)).Converter;
}

// Writing objects with "$type" first (LEX-54, LEX-57).
internal static class LexiconJson
{
    private static ReadOnlySpan<byte> TypePrefix => "{\"$type\":"u8;

    // The value through its own contract, with "$type" inserted after the opening brace; records already lead with it.
    public static byte[] SerializeWithType<T>(T value, JsonTypeInfo<T> typeInfo, string type)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        if (body.AsSpan().StartsWith(TypePrefix)) return body;
        if (body is not [(byte)'{', .., (byte)'}']) throw new JsonException($"{typeof(T).Name} does not serialize as an object.");
        var encoded = JsonEncodedText.Encode(type).EncodedUtf8Bytes;
        var empty = body.Length == 2;
        var result = new byte[TypePrefix.Length + encoded.Length + 2 + (empty ? 1 : body.Length)];
        var span = result.AsSpan();
        TypePrefix.CopyTo(span);
        span = span[TypePrefix.Length..];
        span[0] = (byte)'"';
        encoded.CopyTo(span[1..]);
        span = span[(encoded.Length + 1)..];
        span[0] = (byte)'"';
        span = span[1..];
        if (empty) span[0] = (byte)'}';
        else
        {
            span[0] = (byte)',';
            body.AsSpan(1).CopyTo(span[1..]);
        }
        return result;
    }
}
