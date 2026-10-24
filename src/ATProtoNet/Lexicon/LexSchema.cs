using System.Collections.Generic;
using System.Text.Json;

// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26): netstandard2.0 APIs only. Types are declared without an
// accessibility modifier; LexiconModel.Public.cs makes them public in ATProtoNet.
namespace ATProtoNet.Lexicon;

/// <summary>A schema node of a Lexicon document: one per Lexicon <c>type</c>.</summary>
abstract partial record LexSchema
{
    /// <summary>The node's <c>description</c>.</summary>
    public string? Description { get; init; }

    /// <summary>The node exactly as it appears in the source document.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>A schema node that may also appear under <c>defs</c>.</summary>
abstract partial record LexUserType : LexSchema;

/// <summary>A <c>record</c> definition.</summary>
sealed partial record LexRecord : LexUserType
{
    /// <summary>The record key type, from <c>key</c>.</summary>
    public required RecordKeyType Key { get; init; }

    /// <summary>The record's object schema.</summary>
    public required LexObject Record { get; init; }
}

/// <summary>A <c>query</c> definition.</summary>
sealed partial record LexQuery : LexUserType
{
    /// <summary>The query parameters, if any.</summary>
    public LexParams? Parameters { get; init; }

    /// <summary>The response body, if any.</summary>
    public LexBody? Output { get; init; }

    /// <summary>The declared errors.</summary>
    public IReadOnlyList<LexError> Errors { get; init; } = [];
}

/// <summary>A <c>procedure</c> definition.</summary>
sealed partial record LexProcedure : LexUserType
{
    /// <summary>The query parameters, if any.</summary>
    public LexParams? Parameters { get; init; }

    /// <summary>The request body, if any.</summary>
    public LexBody? Input { get; init; }

    /// <summary>The response body, if any.</summary>
    public LexBody? Output { get; init; }

    /// <summary>The declared errors.</summary>
    public IReadOnlyList<LexError> Errors { get; init; } = [];
}

/// <summary>A <c>subscription</c> definition (an event stream).</summary>
sealed partial record LexSubscription : LexUserType
{
    /// <summary>The query parameters, if any.</summary>
    public LexParams? Parameters { get; init; }

    /// <summary>The message schema, if any: a union of the message types.</summary>
    public LexBody? Message { get; init; }

    /// <summary>The declared errors.</summary>
    public IReadOnlyList<LexError> Errors { get; init; } = [];
}

/// <summary>A <c>permission-set</c> definition.</summary>
sealed partial record LexPermissionSet : LexUserType
{
    /// <summary>The short name shown to users.</summary>
    public string? Title { get; init; }

    /// <summary>Localized titles (<c>title:lang</c>), by language, as written.</summary>
    public IReadOnlyDictionary<string, string>? TitleLang { get; init; }

    /// <summary>What the set grants, shown to users.</summary>
    public string? Detail { get; init; }

    /// <summary>Localized details (<c>detail:lang</c>), by language, as written.</summary>
    public IReadOnlyDictionary<string, string>? DetailLang { get; init; }

    /// <summary>The permissions the set grants.</summary>
    public IReadOnlyList<LexPermission> Permissions { get; init; } = [];
}

/// <summary>One permission of a <see cref="LexPermissionSet"/>.</summary>
sealed partial record LexPermission
{
    /// <summary>The <c>resource</c> (<c>repo</c>, <c>rpc</c>, …), or <see langword="null"/> when missing.</summary>
    public string? Resource { get; init; }

    /// <summary>The permission object as written.</summary>
    public JsonElement Raw { get; init; }
}

/// <summary>A <c>space</c> definition: a space type and the collections its spaces hold.</summary>
sealed partial record LexSpace : LexUserType
{
    /// <summary>The space key type, from <c>key</c>, if declared.</summary>
    public string? Key { get; init; }

    /// <summary>The name shown on consent screens.</summary>
    public string? Name { get; init; }

    /// <summary>Localized names (<c>name:lang</c>), by language, as written.</summary>
    public IReadOnlyDictionary<string, string>? NameLang { get; init; }

    /// <summary>The collections spaces of this type hold, as written.</summary>
    public IReadOnlyList<string> Collections { get; init; } = [];
}

/// <summary>A <c>token</c> definition: a named value with no data.</summary>
sealed partial record LexToken : LexUserType;

/// <summary>An <c>object</c> schema.</summary>
sealed partial record LexObject : LexUserType
{
    /// <summary>The properties, in declaration order.</summary>
    public required IReadOnlyDictionary<string, LexSchema> Properties { get; init; }

    /// <summary>The required property names.</summary>
    public IReadOnlyList<string> Required { get; init; } = [];

    /// <summary>The property names that may be <c>null</c>.</summary>
    public IReadOnlyList<string> Nullable { get; init; } = [];
}

/// <summary>A <c>params</c> schema: query parameters of a method.</summary>
sealed partial record LexParams : LexUserType
{
    /// <summary>The parameters, in declaration order.</summary>
    public required IReadOnlyDictionary<string, LexSchema> Properties { get; init; }

    /// <summary>The required parameter names.</summary>
    public IReadOnlyList<string> Required { get; init; } = [];
}

/// <summary>A <c>string</c> schema.</summary>
sealed partial record LexString : LexUserType
{
    /// <summary>The <c>format</c>, if any.</summary>
    public string? Format { get; init; }

    /// <summary>The minimum length in UTF-8 bytes.</summary>
    public long? MinLength { get; init; }

    /// <summary>The maximum length in UTF-8 bytes.</summary>
    public long? MaxLength { get; init; }

    /// <summary>The minimum length in grapheme clusters.</summary>
    public long? MinGraphemes { get; init; }

    /// <summary>The maximum length in grapheme clusters.</summary>
    public long? MaxGraphemes { get; init; }

    /// <summary>The closed set of allowed values (<c>enum</c>).</summary>
    public IReadOnlyList<string>? Enum { get; init; }

    /// <summary>The open set of suggested values (<c>knownValues</c>).</summary>
    public IReadOnlyList<string>? KnownValues { get; init; }

    /// <summary>The only allowed value (<c>const</c>).</summary>
    public string? Const { get; init; }

    /// <summary>The value the server assumes when absent (<c>default</c>).</summary>
    public string? Default { get; init; }
}

/// <summary>An <c>integer</c> schema.</summary>
sealed partial record LexInteger : LexUserType
{
    /// <summary>The minimum value.</summary>
    public long? Minimum { get; init; }

    /// <summary>The maximum value.</summary>
    public long? Maximum { get; init; }

    /// <summary>The closed set of allowed values.</summary>
    public IReadOnlyList<long>? Enum { get; init; }

    /// <summary>The only allowed value.</summary>
    public long? Const { get; init; }

    /// <summary>The value the server assumes when absent.</summary>
    public long? Default { get; init; }
}

/// <summary>A <c>boolean</c> schema.</summary>
sealed partial record LexBoolean : LexUserType
{
    /// <summary>The only allowed value.</summary>
    public bool? Const { get; init; }

    /// <summary>The value the server assumes when absent.</summary>
    public bool? Default { get; init; }
}

/// <summary>A <c>bytes</c> schema.</summary>
sealed partial record LexBytes : LexUserType
{
    /// <summary>The minimum length in bytes.</summary>
    public long? MinLength { get; init; }

    /// <summary>The maximum length in bytes.</summary>
    public long? MaxLength { get; init; }
}

/// <summary>A <c>cid-link</c> schema.</summary>
sealed partial record LexCidLink : LexUserType;

/// <summary>A <c>blob</c> schema.</summary>
sealed partial record LexBlob : LexUserType
{
    /// <summary>The accepted MIME types; patterns may end in <c>*</c>.</summary>
    public IReadOnlyList<string>? Accept { get; init; }

    /// <summary>The maximum size in bytes.</summary>
    public long? MaxSize { get; init; }
}

/// <summary>An <c>array</c> schema.</summary>
sealed partial record LexArray : LexUserType
{
    /// <summary>The schema of each item.</summary>
    public required LexSchema Items { get; init; }

    /// <summary>The minimum number of items.</summary>
    public long? MinLength { get; init; }

    /// <summary>The maximum number of items.</summary>
    public long? MaxLength { get; init; }
}

/// <summary>An <c>unknown</c> schema: any data-model object.</summary>
sealed partial record LexUnknown : LexUserType;

/// <summary>A <c>ref</c> schema: another definition, by reference.</summary>
sealed partial record LexRef : LexUserType
{
    /// <summary>The reference as written: <c>nsid</c>, <c>nsid#name</c> or <c>#name</c>.</summary>
    public required string Ref { get; init; }
}

/// <summary>A <c>union</c> schema: one of several object definitions, told apart by <c>$type</c>.</summary>
sealed partial record LexUnion : LexUserType
{
    /// <summary>The member references as written.</summary>
    public required IReadOnlyList<string> Refs { get; init; }

    /// <summary>Whether only the listed members are allowed.</summary>
    public bool Closed { get; init; }
}

/// <summary>A node whose <c>type</c> this version does not know; kept so newer schemas stay readable (LEX-4).</summary>
sealed partial record LexUnknownType : LexUserType
{
    /// <summary>The <c>type</c> as written.</summary>
    public required string TypeName { get; init; }
}

/// <summary>The body of a method or the message of a subscription.</summary>
sealed partial record LexBody
{
    /// <summary>The MIME type (<c>encoding</c>); empty for a subscription message.</summary>
    public required string Encoding { get; init; }

    /// <summary>The body's <c>description</c>.</summary>
    public string? Description { get; init; }

    /// <summary>The JSON schema of the body (an object, ref or union), or <see langword="null"/> for none.</summary>
    public LexSchema? Schema { get; init; }
}

/// <summary>An error a method declares.</summary>
/// <param name="Name">The error name, as returned in the <c>error</c> field.</param>
/// <param name="Description">The error's description.</param>
sealed partial record LexError(string Name, string? Description);
