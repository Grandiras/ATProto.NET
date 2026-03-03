using System.Text.Json;
using System.Text.Json.Serialization;

namespace ATProtoNet.LexiconGenerator.Schema;

/// <summary>
/// A Lexicon schema document — the top-level JSON object in a <c>.json</c> schema file.
/// See: https://atproto.com/specs/lexicon
/// </summary>
public sealed class LexiconDocument
{
    [JsonPropertyName("lexicon")]
    public int Lexicon { get; set; } = 1;

    /// <summary>The fully-qualified NSID for this schema (e.g., "app.bsky.feed.post").</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("revision")]
    public int? Revision { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Named definitions within this document. The "main" key is the primary definition.</summary>
    [JsonPropertyName("defs")]
    public Dictionary<string, LexiconSchema> Defs { get; set; } = new();
}

/// <summary>
/// A single schema node in a Lexicon document. The <see cref="Type"/> field determines
/// which other properties are meaningful (flat union — mirrors the JSON representation).
/// 
/// Definition types: record, query, procedure, subscription, object, string, token, boolean, integer, blob, array, ref, union.
/// Property types: string, integer, boolean, blob, bytes, array, ref, union, object, unknown, cid-link.
/// </summary>
public sealed class LexiconSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    // ── record ───────────────────────────────────────────────
    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("record")]
    public LexiconSchema? Record { get; set; }

    // ── object ───────────────────────────────────────────────
    [JsonPropertyName("required")]
    public List<string>? Required { get; set; }

    [JsonPropertyName("nullable")]
    public List<string>? Nullable { get; set; }

    [JsonPropertyName("properties")]
    public Dictionary<string, LexiconSchema>? Properties { get; set; }

    // ── query / procedure ────────────────────────────────────
    [JsonPropertyName("parameters")]
    public LexiconSchema? Parameters { get; set; }

    [JsonPropertyName("input")]
    public LexiconBody? Input { get; set; }

    [JsonPropertyName("output")]
    public LexiconBody? Output { get; set; }

    [JsonPropertyName("errors")]
    public List<LexiconError>? Errors { get; set; }

    // ── subscription ─────────────────────────────────────────
    [JsonPropertyName("message")]
    public LexiconBody? Message { get; set; }

    // ── string constraints ───────────────────────────────────
    [JsonPropertyName("format")]
    public string? Format { get; set; }

    [JsonPropertyName("minLength")]
    public int? MinLength { get; set; }

    [JsonPropertyName("maxLength")]
    public int? MaxLength { get; set; }

    [JsonPropertyName("minGraphemes")]
    public int? MinGraphemes { get; set; }

    [JsonPropertyName("maxGraphemes")]
    public int? MaxGraphemes { get; set; }

    [JsonPropertyName("enum")]
    public List<string>? Enum { get; set; }

    [JsonPropertyName("knownValues")]
    public List<string>? KnownValues { get; set; }

    [JsonPropertyName("default")]
    public JsonElement? Default { get; set; }

    [JsonPropertyName("const")]
    public JsonElement? Const { get; set; }

    // ── integer constraints ──────────────────────────────────
    [JsonPropertyName("minimum")]
    public long? Minimum { get; set; }

    [JsonPropertyName("maximum")]
    public long? Maximum { get; set; }

    // ── array ────────────────────────────────────────────────
    [JsonPropertyName("items")]
    public LexiconSchema? Items { get; set; }

    // ── ref ──────────────────────────────────────────────────
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    // ── union ────────────────────────────────────────────────
    [JsonPropertyName("refs")]
    public List<string>? Refs { get; set; }

    [JsonPropertyName("closed")]
    public bool? Closed { get; set; }

    // ── blob ─────────────────────────────────────────────────
    [JsonPropertyName("accept")]
    public List<string>? Accept { get; set; }

    [JsonPropertyName("maxSize")]
    public long? MaxSize { get; set; }
}

/// <summary>
/// An input/output/message body definition in a query, procedure, or subscription.
/// </summary>
public sealed class LexiconBody
{
    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    [JsonPropertyName("schema")]
    public LexiconSchema? Schema { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

/// <summary>
/// An error definition returned by a query or procedure.
/// </summary>
public sealed class LexiconError
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
