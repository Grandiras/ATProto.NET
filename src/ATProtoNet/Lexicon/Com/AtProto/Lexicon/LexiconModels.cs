using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Identity;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.Lexicon;

// ──────────────────────────────────────────────────────────────
//  com.atproto.lexicon.schema
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A Lexicon schema published as a record (<c>com.atproto.lexicon.schema</c>), keyed by its NSID
/// in the repository its authority's <c>_lexicon</c> DNS record names.
/// </summary>
/// <remarks>
/// The meta-schema only requires <see cref="Lexicon"/>; in practice the record carries the same
/// fields as a Lexicon file. The definitions are kept as raw JSON: the schema language is not
/// itself described in Lexicon. See https://atproto.com/specs/lexicon#lexicon-publication-and-resolution.
/// </remarks>
public sealed class LexiconSchemaRecord : LexObject, IAtProtoRecord
{
    /// <summary>The collection schema records are stored in (<c>com.atproto.lexicon.schema</c>).</summary>
    public static Nsid Collection { get; } = Nsid.Parse("com.atproto.lexicon.schema");

    /// <summary>The Lexicon type discriminator (<c>com.atproto.lexicon.schema</c>).</summary>
    [JsonPropertyName("$type")]
    public string Type => Collection;

    /// <summary>The Lexicon language version; <c>1</c> for the current language.</summary>
    [JsonPropertyName("lexicon")]
    public required int Lexicon { get; init; }

    /// <summary>The schema's NSID, which is also the record key.</summary>
    [JsonPropertyName("id")]
    public Nsid? Id { get; init; }

    /// <summary>The schema's revision, if it declares one.</summary>
    [JsonPropertyName("revision")]
    public int? Revision { get; init; }

    /// <summary>A short overview of the schema.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The schema's definitions, by name, as raw JSON.</summary>
    [JsonPropertyName("defs")]
    public IReadOnlyDictionary<string, JsonElement>? Defs { get; init; }

    /// <summary>
    /// The <c>type</c> of the <c>main</c> definition (<c>record</c>, <c>query</c>,
    /// <c>permission-set</c>, …), or <see langword="null"/> when there is none.
    /// </summary>
    [JsonIgnore]
    public string? MainType =>
        Defs is not null && Defs.TryGetValue("main", out var main) && main.ValueKind == JsonValueKind.Object &&
        main.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

    /// <summary>
    /// The <c>main</c> definition as a permission set.
    /// </summary>
    /// <returns>The permission set, or <see langword="null"/> when <c>main</c> is not one.</returns>
    /// <exception cref="JsonException">The <c>main</c> definition is a permission set but is malformed.</exception>
    public LexiconPermissionSet? GetPermissionSet() =>
        MainType == LexiconPermissionSet.DefinitionType
            ? Defs!["main"].Deserialize<LexiconPermissionSet>(AtProtoJsonDefaults.Options)
              ?? throw new JsonException("The permission set definition is null.")
            : null;
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.lexicon.resolveLexicon
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A resolved Lexicon schema: the schema record and where it was found. The output of
/// <c>com.atproto.lexicon.resolveLexicon</c>, and of every <see cref="ILexiconResolver"/>.
/// </summary>
public sealed class ResolvedLexicon
{
    /// <summary>The AT URI of the schema record.</summary>
    [JsonPropertyName("uri")]
    public required AtUri Uri { get; init; }

    /// <summary>The CID of the schema record.</summary>
    [JsonPropertyName("cid")]
    public required Cid Cid { get; init; }

    /// <summary>The schema record.</summary>
    [JsonPropertyName("schema")]
    public required LexiconSchemaRecord Schema { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Permission sets (the permission-set definition type)
// ──────────────────────────────────────────────────────────────

/// <summary>
/// A permission set: a bundle of OAuth permissions an app requests with one
/// <c>include:&lt;nsid&gt;</c> scope, published as the <c>main</c> definition of a Lexicon.
/// </summary>
/// <remarks>
/// See https://atproto.com/specs/permission#permission-sets. An authorization server ignores any
/// permission in a set that names a resource or parameter it does not know, or that reaches
/// outside the set's own NSID namespace.
/// </remarks>
public sealed class LexiconPermissionSet : LexObject
{
    /// <summary>The definition <c>type</c> of a permission set (<c>permission-set</c>).</summary>
    public const string DefinitionType = "permission-set";

    /// <summary>A short name for the set, shown to users.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>Localized <see cref="Title"/> values, keyed by language code.</summary>
    [JsonPropertyName("title:lang")]
    public IReadOnlyDictionary<string, string>? LocalizedTitles { get; init; }

    /// <summary>What the set grants, in a paragraph or so, shown to users.</summary>
    [JsonPropertyName("detail")]
    public string? Detail { get; init; }

    /// <summary>Localized <see cref="Detail"/> values, keyed by language code.</summary>
    [JsonPropertyName("detail:lang")]
    public IReadOnlyDictionary<string, string>? LocalizedDetails { get; init; }

    /// <summary>A description for developers.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>The permissions the set grants.</summary>
    [JsonPropertyName("permissions")]
    public required IReadOnlyList<LexiconPermission> Permissions { get; init; }
}

/// <summary>
/// One permission of a <see cref="LexiconPermissionSet"/>, in its Lexicon form.
/// </summary>
/// <remarks>
/// Values are kept as published, not parsed into identifier types: a set may carry permissions an
/// authorization server has to ignore (a wildcard, an unknown resource), and those must not make
/// the rest of the set unreadable. Parameters this model does not name are kept in
/// <see cref="LexObject.ExtensionData"/>.
/// </remarks>
public sealed class LexiconPermission : LexObject
{
    /// <summary>The resource the permission grants access to: <c>repo</c> or <c>rpc</c>.</summary>
    [JsonPropertyName("resource")]
    public required string Resource { get; init; }

    /// <summary><c>repo</c>: the record collections (NSIDs) the permission covers.</summary>
    [JsonPropertyName("collection")]
    public IReadOnlyList<string>? Collection { get; init; }

    /// <summary><c>repo</c>: the record operations allowed (<c>create</c>, <c>update</c>, <c>delete</c>); all when absent.</summary>
    [JsonPropertyName("action")]
    public IReadOnlyList<string>? Action { get; init; }

    /// <summary><c>rpc</c>: the XRPC methods (NSIDs) the permission covers.</summary>
    [JsonPropertyName("lxm")]
    public IReadOnlyList<string>? Lxm { get; init; }

    /// <summary><c>rpc</c>: the audience; in a permission set only <c>*</c> is allowed.</summary>
    [JsonPropertyName("aud")]
    public string? Aud { get; init; }

    /// <summary>
    /// <c>rpc</c>: whether the audience comes from the <c>aud</c> parameter of the
    /// <c>include:</c> scope that requested the set.
    /// </summary>
    [JsonPropertyName("inheritAud")]
    public bool? InheritAud { get; init; }
}
