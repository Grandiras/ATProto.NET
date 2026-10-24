using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Runtime;

/// <summary>Reads any JSON string into a known-values struct and writes its string back (LEX-47).</summary>
/// <typeparam name="T">The generated struct.</typeparam>
public sealed class KnownValueJsonConverter<T> : JsonConverter<T> where T : struct, IKnownValue<T>
{
    /// <inheritdoc />
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String ? T.Create(reader.GetString()!) : throw new JsonException($"A {typeof(T).Name} is a JSON string, not {reader.TokenType}.");

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value ?? throw new JsonException($"A default {typeof(T).Name} has no value to write."));
}

/// <summary>A property that may be absent, present with a value, or present as <c>null</c>: a Lexicon field that is <c>nullable</c> but not required (LEX-40).</summary>
/// <typeparam name="T">The value type.</typeparam>
/// <remarks><see langword="default"/> is absent and is not written; <c>new Optional&lt;Cid?&gt;(null)</c> writes <c>null</c>.</remarks>
public readonly record struct Optional<T>
{
    /// <summary>A present value.</summary>
    /// <param name="value">The value, possibly <see langword="null"/>.</param>
    public Optional(T value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>Whether the property is present.</summary>
    public bool HasValue { get; }

    /// <summary>The value when present; <see langword="default"/> otherwise.</summary>
    public T Value { get; }

    /// <summary>A present value.</summary>
    /// <param name="value">The value.</param>
    public static implicit operator Optional<T>(T value) => new(value);

    /// <inheritdoc />
    public override string ToString() => HasValue ? $"{Value}" : "(absent)";
}

/// <summary>Reads and writes an <see cref="Optional{T}"/> property; generated code pairs it with omission when absent.</summary>
/// <typeparam name="T">The value type.</typeparam>
public sealed class OptionalJsonConverter<T> : JsonConverter<Optional<T>>
{
    /// <summary>JSON <c>null</c> reaches <see cref="Read"/>, as a present null.</summary>
    public override bool HandleNull => true;

    /// <inheritdoc />
    public override Optional<T> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.Null ? new(default!) : new(Inner(options).Read(ref reader, typeof(T), options)!);

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, Optional<T> value, JsonSerializerOptions options)
    {
        if (value.Value is null) writer.WriteNullValue();
        else Inner(options).Write(writer, value.Value, options);
    }

    // The value's own converter, called directly so errors keep the outer JSON path.
    private static JsonConverter<T> Inner(JsonSerializerOptions options) => (JsonConverter<T>)options.GetTypeInfo(typeof(T)).Converter;
}
