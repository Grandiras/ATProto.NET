using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.Spaces;

/// <summary>
/// A space type declaration: the Lexicon definition a space type NSID resolves to.
/// </summary>
/// <remarks>
/// <para>A space type names the <em>modality</em> of a space — a forum, a set of bookmarks, a
/// group chat — and identifies the kind of data it holds before any network resolution, much as
/// a collection NSID does in public AT Protocol. Because the type names a concrete modality,
/// every space is some specific kind of space rather than a generic container.</para>
/// <para>It is also the OAuth consent boundary. A <c>space:</c> scope grants access <em>by
/// type</em>, and the consent screen shows this declaration's <see cref="Name"/> in place of the
/// raw NSID — "access to your AtmoBoards forums" rather than
/// "access to com.atmoboards.forum".</para>
/// <para>The declaration is the <c>main</c> definition of a Lexicon document, with
/// <c>"type": "space"</c> where a record schema would say <c>"record"</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// {
///   "lexicon": 1,
///   "id": "com.atmoboards.forum",
///   "defs": {
///     "main": {
///       "type": "space",
///       "key": "any",
///       "name": "AtmoBoards Forum",
///       "collections": ["com.atmoboards.thread", "com.atmoboards.reply"]
///     }
///   }
/// }
/// </code>
/// </example>
public sealed class SpaceTypeDeclaration
{
    /// <summary>The Lexicon definition type. Always <c>space</c>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "space";

    /// <summary>
    /// A description of the space type for developers. Not shown to users.
    /// </summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>
    /// The recommended space key type, in the same vocabulary as a record key type
    /// (e.g. <c>tid</c>, <c>any</c>, <c>literal:self</c>).
    /// </summary>
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    /// <summary>
    /// The human-readable name shown to users on OAuth consent screens (1–64 characters).
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Localized <see cref="Name"/> values, keyed by language code.</summary>
    [JsonPropertyName("name:lang")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? LocalizedNames { get; init; }

    /// <summary>
    /// The collections clients should expect in a space of this type.
    /// </summary>
    /// <remarks>
    /// This is a recommendation and the default collection set for a bare <c>space:</c> scope of
    /// this type — not a constraint. Any collection may be written to any space; the protocol
    /// does not restrict it.
    /// </remarks>
    [JsonPropertyName("collections")]
    public required List<string> Collections { get; init; }

    /// <summary>
    /// Returns the localized name for a language, falling back to <see cref="Name"/>.
    /// </summary>
    /// <param name="language">The language code to look for.</param>
    public string GetName(string? language) =>
        language is not null &&
        LocalizedNames is not null &&
        LocalizedNames.TryGetValue(language, out var localized)
            ? localized
            : Name;

    /// <summary>
    /// Extracts the space type declaration from a Lexicon document's <c>main</c> definition.
    /// </summary>
    /// <param name="lexicon">The parsed Lexicon document.</param>
    /// <returns>The declaration, or <see langword="null"/> when the document does not declare a space type.</returns>
    public static SpaceTypeDeclaration? FromLexicon(JsonElement lexicon)
    {
        if (lexicon.ValueKind != JsonValueKind.Object ||
            !lexicon.TryGetProperty("defs", out var defs) ||
            defs.ValueKind != JsonValueKind.Object ||
            !defs.TryGetProperty("main", out var main) ||
            main.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        // A space type must be the `main` definition, so a document whose main is a record or a
        // query simply is not one — not an error.
        if (!main.TryGetProperty("type", out var type) ||
            type.ValueKind != JsonValueKind.String ||
            type.GetString() != "space")
        {
            return null;
        }

        return main.Deserialize<SpaceTypeDeclaration>();
    }
}
