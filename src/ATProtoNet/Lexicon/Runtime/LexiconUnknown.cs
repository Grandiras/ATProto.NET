using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace ATProtoNet.Lexicon.Runtime;

/// <summary>
/// The extra case of every open union (LEX-50): an object whose <c>$type</c> the union does not list, kept as received and written
/// back unchanged.
/// </summary>
/// <remarks>
/// A user type that extends an SDK union travels as this case: <see cref="TryAs{T}"/> reads it as that type and
/// <see cref="From{T}"/> builds it for writing (LEX-86).
/// </remarks>
public sealed class LexiconUnknown
{
    /// <summary>Creates the case from an object with a string <c>$type</c>.</summary>
    /// <param name="raw">The object; it is copied, so the caller's document may be disposed.</param>
    /// <exception cref="ArgumentException"><paramref name="raw"/> is not an object with a non-empty string <c>$type</c>.</exception>
    public LexiconUnknown(JsonElement raw)
    {
        if (raw.ValueKind != JsonValueKind.Object || !raw.TryGetProperty("$type", out var type) || type.ValueKind != JsonValueKind.String
            || type.GetString() is not { Length: > 0 } text)
            throw new ArgumentException("A union member is an object with a non-empty string $type.", nameof(raw));
        Type = text;
        Raw = raw.Clone();
    }

    /// <summary>The <c>$type</c> as received.</summary>
    public string Type { get; }

    /// <summary>The object, detached from any document.</summary>
    public JsonElement Raw { get; }

    /// <summary>The case for a value of a type the union does not list, such as a user's own embed (LEX-86).</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>The case, with <c>$type</c> first and then the value's members.</returns>
    public static LexiconUnknown From<T>(T value) where T : class, ILexiconType<T>
    {
        ArgumentNullException.ThrowIfNull(value);
        var bytes = LexiconJson.SerializeWithType(value, T.JsonTypeInfo, T.LexiconType);
        using var document = JsonDocument.Parse(bytes);
        return new(document.RootElement);
    }

    /// <summary>Reads the case as <typeparamref name="T"/> when its <c>$type</c> is that type's.</summary>
    /// <typeparam name="T">The type.</typeparam>
    /// <param name="value">The value.</param>
    /// <returns>Whether <see cref="Type"/> is <typeparamref name="T"/>'s <c>$type</c>.</returns>
    /// <exception cref="JsonException">The type matches but the object does not fit <typeparamref name="T"/>.</exception>
    public bool TryAs<T>([NotNullWhen(true)] out T? value) where T : class, ILexiconType<T>
    {
        value = Type == T.LexiconType ? Raw.Deserialize(T.JsonTypeInfo) ?? throw new JsonException("A union member is not null.") : null;
        return value is not null;
    }

    /// <inheritdoc />
    public override string ToString() => $"LexiconUnknown {{ $type = {Type} }}";
}
