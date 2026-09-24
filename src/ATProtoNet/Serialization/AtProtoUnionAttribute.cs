using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Serialization;

/// <summary>
/// Marks an abstract class as the base of a Lexicon union, discriminated by <c>$type</c>.
/// </summary>
/// <remarks>
/// <para>Declare the variants you know with <see cref="JsonDerivedTypeAttribute"/> on the base, using the
/// discriminator the Lexicon defines (<c>&lt;nsid&gt;#&lt;defName&gt;</c>, or the bare NSID for a
/// <c>main</c> def). Add variants you do not own at runtime with
/// <see cref="LexiconTypeRegistry.RegisterUnionVariant{TBase, TDerived}(string)"/>.</para>
/// <para>Lexicon unions are <b>open</b> unless the schema says <c>"closed": true</c>. An open union
/// names an <see cref="UnknownVariant"/>: a <c>$type</c> that is neither declared nor registered
/// deserializes to that variant, which keeps the raw object and writes it back byte-for-byte. A
/// <see cref="Closed"/> union rejects an unknown <c>$type</c> with a <see cref="JsonException"/>.</para>
/// <para>The attribute takes effect through <see cref="AtProtoJsonDefaults.Options"/>, which every SDK
/// client uses. Options built from scratch fall back to <c>System.Text.Json</c>'s own polymorphism,
/// which reads the declared variants but rejects unknown ones; start from a copy of
/// <see cref="AtProtoJsonDefaults.Options"/> instead.</para>
/// </remarks>
/// <example>
/// <code>
/// [AtProtoUnion(typeof(UnknownAttribution))]
/// [JsonDerivedType(typeof(AuthorAttribution), "com.example.recipe.defs#attributionAuthor")]
/// public abstract class Attribution : LexObject;
///
/// public sealed class UnknownAttribution(string type, JsonElement raw)
///     : Attribution, IUnknownUnionVariant
/// {
///     public string Type { get; } = type;
///     public JsonElement Raw { get; } = raw;
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AtProtoUnionAttribute : Attribute
{
    /// <summary>
    /// Marks a closed union. Set <see cref="Closed"/> to <see langword="true"/>; an open union
    /// must use the <see cref="AtProtoUnionAttribute(Type)"/> overload instead.
    /// </summary>
    public AtProtoUnionAttribute()
    {
    }

    /// <summary>Marks an open union whose unrecognized variants deserialize to <paramref name="unknownVariant"/>.</summary>
    /// <param name="unknownVariant">
    /// A sealed subclass of the union base that implements <see cref="IUnknownUnionVariant"/> and has a
    /// public <c>(string type, JsonElement raw)</c> constructor.
    /// </param>
    public AtProtoUnionAttribute(Type unknownVariant)
    {
        ArgumentNullException.ThrowIfNull(unknownVariant);
        UnknownVariant = unknownVariant;
    }

    /// <summary>The variant an unrecognized <c>$type</c> deserializes to, or <see langword="null"/> for a closed union.</summary>
    public Type? UnknownVariant { get; }

    /// <summary>
    /// Whether the Lexicon marks the union <c>"closed": true</c>, so that an unrecognized <c>$type</c>
    /// is an error rather than an <see cref="UnknownVariant"/>.
    /// </summary>
    public bool Closed { get; set; }
}

/// <summary>
/// A union variant whose <c>$type</c> the SDK did not recognize when it read the data.
/// </summary>
/// <remarks>
/// <para>Every open union in the SDK has one, named <c>Unknown{Base}</c>. It holds the whole object
/// as it arrived, <c>$type</c> included, and serializing it writes <see cref="Raw"/> back
/// byte-for-byte, so a read-modify-write never loses a variant this SDK version does not model.
/// Match it in a <c>switch</c> to handle, or skip, content from newer schemas.</para>
/// <para>An unknown variant inherits <c>ExtensionData</c> from its base; it is always
/// <see langword="null"/>, since <see cref="Raw"/> already holds every field.</para>
/// </remarks>
public interface IUnknownUnionVariant
{
    /// <summary>The <c>$type</c> discriminator the object carried.</summary>
    string Type { get; }

    /// <summary>The complete JSON object, including <c>$type</c>.</summary>
    JsonElement Raw { get; }
}
