# Lexicon: schema model, validation, code generation

**Status:** Draft · **Packages:** `ATProtoNet.Lexicon` (schema model, catalog, lint and
compatibility rules, validation, generated-code runtime), `ATProtoNet.Lexicon.CodeGen` (emitter),
`ATProtoNet.Lexicon.Generator` (Roslyn), and the generated code checked into `ATProtoNet`,
`ATProtoNet.Bluesky`, `ATProtoNet.Ozone`, `ATProtoNet.StandardSite`, `ATProtoNet.Spaces`; runtime
Lexicon resolution in `ATProtoNet` · **Phase:** 3 (model, validation, CodeGen, first-party
`com.atproto` generation), 6 (runtime resolution), 8 (source generator, remaining Lexicon
packages) · **Normative references:** [Lexicon](https://atproto.com/specs/lexicon),
[Lexicon versioning](https://atproto.com/guides/lexicon#versioning),
[Data model](https://atproto.com/specs/data-model), [NSID](https://atproto.com/specs/nsid),
[Record key](https://atproto.com/specs/record-key),
[Installing Lexicons](https://atproto.com/guides/installing-lexicons),
[`@atproto/lex`](https://github.com/bluesky-social/atproto/blob/main/packages/lex/lex/README.md)

## Scope

In scope:

- the one Lexicon schema object model, shared by the runtime, the code generator, the CLI and
  the tests;
- cross-document and cross-assembly reference resolution;
- lint rules, compatibility (diff) rules and data validation;
- the vendored `lexicons/` tree and its `lexicons.json` manifest;
- generator architecture, naming, and the complete specification of generated C#;
- how hand-written code sits on top of generated code, and the path for users' own Lexicons;
- runtime Lexicon resolution (`com.atproto.lexicon.schema` records, verified).

Out of scope: CLI commands and UX ([tooling.md](tooling.md)); the XRPC pipeline, descriptor
execution, pagination helper and records API surface ([xrpc-client.md](xrpc-client.md)); server
binding of generated descriptors ([server.md](server.md)); DRISL-CBOR, `Cid`, `BlobRef`,
`CidLink` and `DataModelValue` ([data-model.md](data-model.md)); hand-written CBOR binders for
event-stream envelopes ([streaming.md](streaming.md)); identifier syntax ([syntax.md](syntax.md));
OAuth scope parsing of permission sets ([auth.md](auth.md)).

## Public API sketch

```csharp
// ── Schema object model (ATProtoNet.Lexicon) ─────────────────────────────────────────────
namespace ATProtoNet.Lexicon;

public sealed class LexiconDocument                       // immutable; keeps the source JSON
{
    public int LexiconVersion { get; }                    // always 1
    public Nsid Id { get; }
    public int? Revision { get; }
    public string? Description { get; }
    public IReadOnlyDictionary<string, LexUserType> Defs { get; }   // "main" plus named defs
    public ReadOnlyMemory<byte> SourceUtf8 { get; }       // exact bytes parsed, for publishing and hashing
    public static LexiconDocument Parse(ReadOnlySpan<byte> utf8Json);            // FormatException
    public static bool TryParse(ReadOnlySpan<byte> utf8Json, out LexiconDocument? document,
        out IReadOnlyList<LexiconDiagnostic> diagnostics);
}

public abstract record LexSchema { public string? Description { get; init; } public JsonElement Raw { get; init; } }
public abstract record LexUserType : LexSchema;           // what may appear under "defs"
public sealed record LexRecord : LexUserType { public required RecordKeyType Key { get; init; } public required LexObject Record { get; init; } }
public sealed record LexQuery : LexUserType { public LexParams? Parameters { get; init; } public LexBody? Output { get; init; } public IReadOnlyList<LexError> Errors { get; init; } = []; }
public sealed record LexProcedure : LexUserType { public LexParams? Parameters { get; init; } public LexBody? Input { get; init; } public LexBody? Output { get; init; } public IReadOnlyList<LexError> Errors { get; init; } = []; }
public sealed record LexSubscription : LexUserType { public LexParams? Parameters { get; init; } public LexUnion? Message { get; init; } public IReadOnlyList<LexError> Errors { get; init; } = []; }
public sealed record LexPermissionSet : LexUserType { /* title(+lang), detail(+lang), permissions */ }
public sealed record LexSpace : LexUserType { /* experimental (ATPROTO001): key, name(+lang), collections */ }
public sealed record LexToken : LexUserType;
public sealed record LexObject : LexUserType { public IReadOnlyDictionary<string, LexSchema> Properties { get; init; } public IReadOnlyList<string> Required { get; init; } = []; public IReadOnlyList<string> Nullable { get; init; } = []; }
public sealed record LexString : LexUserType { /* format, minLength, maxLength, minGraphemes, maxGraphemes, enum, const, default, knownValues */ }
// also: LexInteger, LexBoolean, LexBytes, LexCidLink, LexBlob, LexArray, LexUnknown (named-def capable),
// LexRef, LexUnion (never named defs), LexParams, LexBody(Encoding, Schema), LexError(Name, Description)

public sealed class LexiconCatalog                        // a closed set of documents plus external definitions
{
    public static LexiconCatalog Create(IEnumerable<LexiconDocument> documents, IExternalLexicons? external = null);
    public bool TryResolve(LexRefTarget target, out ResolvedDefinition definition);  // "nsid", "nsid#name", "#name"
    public IReadOnlyList<LexiconDiagnostic> Diagnostics { get; }                    // unresolved refs, cycles through refs
}
public readonly record struct LexRefTarget(Nsid Nsid, string DefName);           // DefName "main" when omitted

public static class LexiconLint   { public static IReadOnlyList<LexiconDiagnostic> Run(LexiconCatalog catalog); }
public static class LexiconCompatibility
{
    public static LexiconChangeReport Compare(LexiconCatalog baseline, LexiconCatalog current);
}

namespace ATProtoNet.Lexicon.Validation;
public sealed class LexiconValidator
{
    public LexiconValidator(LexiconCatalog catalog);
    public LexiconValidationResult Validate(LexRefTarget definition, DataModelValue value);
    public LexiconValidationResult ValidateRecord(Nsid collection, RecordKey rkey, DataModelValue value);
}

// ── Runtime support for generated code (ATProtoNet.Lexicon.Runtime) ──────────────────────
namespace ATProtoNet.Lexicon.Runtime;

public interface ILexiconObject { IDictionary<string, JsonElement>? ExtensionData { get; } }
public interface IAtProtoRecord : ILexiconObject
{
    static abstract Nsid Collection { get; }
    static abstract RecordKeyType KeyType { get; }        // Tid | Nsid | Any | Literal(value)
}
public interface IAtProtoRecord<TSelf> : IAtProtoRecord where TSelf : IAtProtoRecord<TSelf>
{
    static abstract JsonTypeInfo<TSelf> JsonTypeInfo { get; }
}
public interface ICreatedAtRecord<TSelf> where TSelf : ICreatedAtRecord<TSelf>
{
    AtDatetime? CreatedAt { get; }
    static abstract TSelf WithCreatedAt(TSelf record, AtDatetime createdAt);
}
public interface IUnknownUnionVariant { string Type { get; } JsonElement Raw { get; } }
public interface IKnownValue<TSelf> where TSelf : struct, IKnownValue<TSelf>
{
    string Value { get; }
    bool IsKnown { get; }
    static abstract TSelf Create(string value);
}
public interface ICursorPage<T> { string? Cursor { get; } IReadOnlyList<T> Items { get; } }
public interface ICursorParameters<TSelf> where TSelf : ICursorParameters<TSelf> { string? Cursor { get; init; } }
public readonly record struct Optional<T>;                // absent vs present (possibly null): Lexicon `nullable` + not required
public sealed record PermissionSetRef(Nsid Nsid) { public string Include(XrpcServiceRef? audience = null); }

public sealed class LexiconTypeRegistry                   // an instance, never a global (D-10)
{
    public void AddUnionVariant<TUnion, TVariant>(string type) where TVariant : TUnion;
    public void AddRecord<TRecord>() where TRecord : class, IAtProtoRecord<TRecord>;
    public bool TryGetRecordType(Nsid collection, out JsonTypeInfo typeInfo);
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
public sealed class LexiconDefinitionAttribute(string reference) : Attribute;   // "app.bsky.feed.defs#postView"
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class LexiconAssemblyAttribute(Type lexiconsClass) : Attribute;  // points at the generated {X}Lexicons class
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class LexiconFormatTypeAttribute(string format, Type type) : Attribute; // e.g. "space-ref" → SpaceUri

// DataModelValue (Primitives) gains, through generated metadata:
//   T As<T>() where T : class, IAtProtoRecord<T>;  bool TryAs<T>(out T? value);
//   IAtProtoRecord? AsKnownRecord(LexiconTypeRegistry registry);
```

## Requirements

### Schema object model

- **LEX-1** (MUST) There is exactly one Lexicon schema object model, in `ATProtoNet.Lexicon`. The
  runtime (validation, resolution), CodeGen, the CLI and the tests all use it. No second
  representation of Lexicon documents, permission sets or defs exists anywhere in the codebase.
  *Source:* Decision · 0.8 has three overlapping models (`tools/…/Schema/LexiconDocument.cs`, the
  runtime `LexiconSchemaRecord` with `Defs` as raw `JsonElement` at `LexiconModels.cs:44`, and the
  tests' `UpstreamLexicons`), plus `AtProtoScopes.PermissionSets` and `SdkTypeMap` as mirrors
- **LEX-2** (MUST) The model is immutable and typed: one record type per Lexicon type (`record`,
  `query`, `procedure`, `subscription`, `permission-set`, `token`, `object`, `string`,
  `integer`, `boolean`, `bytes`, `cid-link`, `blob`, `array`, `unknown`, `ref`, `union`,
  `params`), plus `space` behind `[Experimental("ATPROTO001")]`. Every node keeps its source
  `JsonElement`, and every document keeps its exact source bytes, so publishing and hashing never
  re-serialize.
  *Source:* [Lexicon spec](https://atproto.com/specs/lexicon) · Decision · 0.8 #92 (space defs)
- **LEX-3** (MUST) Parsing enforces the document shape: `lexicon` is `1`; `id` is a valid NSID; at
  least one def; at most one primary type (`record`, `query`, `procedure`, `subscription`,
  `permission-set`, `space`), and only under the name `main`; `ref`, `union`, `unknown` and
  `params` cannot be named defs (except `unknown` as allowed by the spec); `params` properties are
  limited to `boolean`, `integer`, `string`, `unknown` and arrays of those. Violations are
  `LexiconDiagnostic`s with a JSON pointer; `Parse` throws `FormatException` naming the first one.
  *Source:* [Lexicon spec: Lexicon Files, params](https://atproto.com/specs/lexicon) · 0.8
  `LexiconLinterTests.Lint_InvalidIdOrNoDefinitions_IsAnError`
- **LEX-4** (MUST) An unknown def or field `type` is preserved as `LexUnknownType` with a warning,
  never a parse failure, so newer schemas stay readable. A JSON `null` where a schema is expected
  is an error wherever it appears.
  *Source:* 0.8 #130 · `Lint_UnknownDefinitionType_IsAWarning`, `Lint_NullType_IsAnErrorWhereverItIs`
- **LEX-5** (MUST) Localized fields (`title:lang`, `detail:lang`, `name:lang`, …) are kept as
  written, including their language keys.
  *Source:* 0.8 `ToDocumentJson_LocalizedText_IsWrittenAsIs`

### Catalog and reference resolution

- **LEX-6** (MUST) `LexiconCatalog` resolves `ref` and `union` targets in three forms: `nsid`
  (meaning `nsid#main`), `nsid#name` and local `#name`. An unresolvable target is a diagnostic with
  the referring pointer, never a silent fallback to `JsonElement`.
  *Source:* [Lexicon spec: ref](https://atproto.com/specs/lexicon) · Decision · 0.8
  `Emit_UnresolvableRef_FallsBackToNullableJsonElementAndWarns` (behaviour dropped)
- **LEX-7** (MUST) A `ref` to a `token` is an error. A `ref` or `union` member that resolves to
  another `ref` or `union` is an error.
  *Source:* [Lexicon spec: ref](https://atproto.com/specs/lexicon)
- **LEX-8** (MUST) Definitions outside the catalog's documents resolve through
  `IExternalLexicons`. The generator and CLI implement it from `[LexiconDefinition]` attributes on
  types in referenced assemblies (Roslyn symbols in the generator, `System.Reflection.Metadata` in
  the CLI, never loading the assembly). This replaces 0.8's hand-maintained `SdkTypeMap`.
  *Source:* Decision · 0.8 `tools/…/CodeGen/SdkTypeMap.cs` (≈45 hand-listed entries)
- **LEX-9** (MUST) Recursive structures through `ref`/`union` (thread views, nested replies) are
  supported. The catalog detects only cycles that cannot terminate (a required, non-nullable,
  non-array self-reference) and reports them.
  *Source:* Decision

### Lint rules

- **LEX-10** (MUST) Lint rules live in `ATProtoNet.Lexicon` (`LexiconLint`), with stable rule IDs
  (`LXL001`…), severity `Error` or `Warning`, a message and a JSON pointer. The CLI's `lint` and
  `publish` and the generator all run the same rules.
  *Source:* Decision · 0.8 `tools/…/Validation/LexiconLinter.cs`
- **LEX-11** (MUST) Permission-set rules: the set is `main`; it has a `title` and at least one
  permission; only `repo`, `rpc` and (if present) `space` resources are allowed in a set; no
  wildcards; every NSID is in the set's own namespace group or below; repo actions are known and
  unique; malformed permissions are errors; unknown resources or parameters are warnings that say
  servers ignore them.
  *Source:* [Permission spec](https://atproto.com/specs/permission) · 0.8 #130 ·
  `LexiconLinterTests.Lint_*` (permission-set cases)
- **LEX-12** (MUST) Space rules (experimental): collections warn on wildcards and non-NSIDs, not on
  long names.
  *Source:* 0.8 #92 · `Lint_SpaceCollections_WarnsOnWildcardAndNonNsids_NotOnLongNames`
- **LEX-13** (SHOULD) Style rules from the Lexicon style guide (camelCase property names, `main`
  for primary types, descriptions present on primary defs, `maxGraphemes` paired with
  `maxLength`) are warnings.
  *Source:* [Lexicon style guide](https://atproto.com/guides/lexicon-style-guide)

### Compatibility (diff) rules

- **LEX-14** (MUST) `LexiconCompatibility.Compare` classifies every change between two catalogs as
  *compatible* or *breaking*, following the Lexicon versioning rule: once published, a schema can
  only add optional constraints to previously unconstrained fields. Breaking: removing a schema,
  def or property; adding a required property; changing a type or string `format`; making a
  property required **or optional**; tightening **or loosening** `minLength`, `maxLength`,
  `minGraphemes`, `maxGraphemes`, `minimum`, `maximum`, array bounds or blob `maxSize`; adding or
  removing `enum` values; removing `refs` from any union; adding `refs` to a closed union;
  changing a record `key`; changing a method's encodings, parameters' types or kind.
  *Source:* [Lexicon versioning](https://atproto.com/guides/lexicon#versioning) · 0.8
  `CodeGen/LexiconDiffer.cs` (see Changes from 0.8)
- **LEX-15** (MUST) Compatible: adding a schema, def or optional property; adding a constraint to a
  previously unconstrained field; adding `refs` to an open union; adding `knownValues`; adding or
  changing descriptions, titles and localized text; adding declared errors.
  *Source:* [Lexicon spec: union](https://atproto.com/specs/lexicon) · Decision
- **LEX-16** (MUST) Space defs: changing `key` is breaking; changing `name` (or a localized name) is
  compatible; adding a collection is compatible (it widens grants); removing one is breaking.
  *Source:* 0.8 #92 · `LexiconDiffer.cs:169-238`
- **LEX-17** (MUST) The report suggests a `revision` bump (current revision or 1, plus 1) for every
  schema with compatible changes only.
  *Source:* 0.8 `LexiconDiffer.SuggestRevisions` · `SuggestRevisions_BumpsNonBreakingSchemas`

### Data validation

- **LEX-18** (MUST) `LexiconValidator` validates a `DataModelValue` against a def and reports every
  violation with its data path. Checked: types; `required`; `nullable` (only listed properties may
  be `null`); string `minLength`/`maxLength` in UTF-8 bytes and `minGraphemes`/`maxGraphemes` in
  extended grapheme clusters; `format` through the Primitives parsers; `enum` and `const`; integer
  `minimum`/`maximum`/`enum`/`const`; array `minLength`/`maxLength`; bytes length; blob `accept`
  (MIME patterns with `*`) and `maxSize`; `$type` of records equals the collection.
  *Source:* [Lexicon spec](https://atproto.com/specs/lexicon), [Data model](https://atproto.com/specs/data-model)
- **LEX-19** (MUST) Open semantics are respected: unknown object properties pass; `knownValues`
  are not enforced; an open union accepts any object with a `$type` (its known variants are
  validated, unknown ones only for being an object with `$type`); a closed union rejects an
  unlisted `$type`; a union member without `$type` is invalid.
  *Source:* [Lexicon spec: union, string](https://atproto.com/specs/lexicon)
- **LEX-20** (MUST) `ValidateRecord` also checks the record key against the record's `key` type
  (`tid`, `nsid`, `literal:<value>`, `any`).
  *Source:* [Record key spec](https://atproto.com/specs/record-key)
- **LEX-21** (SHOULD) Validation is opt-in on the client (records API, see [xrpc-client.md](xrpc-client.md)):
  servers validate authoritatively, and a client with an older schema must not reject newer data.
  The server package validates inputs it serves ([server.md](server.md)).
  *Source:* Decision

### Vendored Lexicons and manifest

- **LEX-22** (MUST) First-party Lexicon JSON lives in `lexicons/` at the repository root, laid out
  as `{nsid segments}/{name}.json`, and is never edited by hand. `lexicons.json` records, per
  source, where it came from (git repository + commit + path, network authority DID, or local
  directory for proposals such as Spaces), and per NSID its source and CID.
  *Source:* [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json) ·
  0.8 `tests/ATProtoNet.Tests/Lexicon/Upstream/README.md` (same provenance, kept in prose)
- **LEX-23** (MUST) The per-NSID CID is the CID of the schema's DRISL-CBOR encoding in record form
  (`{"$type":"com.atproto.lexicon.schema", …document}`), so it equals the CID of the published
  `com.atproto.lexicon.schema` record when the file is published unchanged. Generation fails if a
  vendored file no longer matches its CID.
  *Source:* Decision · [Lexicon resolution](https://atproto.com/specs/lexicon)
- **LEX-24** (MUST) Vendored first-party sources: `bluesky-social/atproto` `lexicons/` for
  `com.atproto.*`, `app.bsky.*`, `chat.bsky.*`, `tools.ozone.*`; the `standard.site` authority's
  published records for `site.standard.*` (the only source of its permission sets); the
  permissioned-data proposal repository at a pinned commit for `com.atproto.space.*` and
  `com.atproto.simplespace.*` (experimental).
  *Source:* 0.8 `Lexicon/Upstream/README.md`, `refresh.sh` · [D-24](../06-decisions.md#d-24-spaces-scope-in-the-rewrite)

### Generator architecture

<a id="generator-architecture"></a>

- **LEX-25** (MUST) One emitter, in `ATProtoNet.Lexicon.CodeGen` (`netstandard2.0`), produces all
  generated C#. The CLI's `generate` and the Roslyn generator both call it; for the same inputs
  and options their output is byte-identical (a golden test asserts this).
  *Source:* [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json)
- **LEX-26** (MUST) CodeGen compiles the schema-model sources of `ATProtoNet.Lexicon` as linked
  files (one source of truth, compiled twice). The linked sources declare their accessibility
  through a conditional symbol so they are `public` in `ATProtoNet.Lexicon` and `internal` in
  CodeGen; CodeGen's public API takes Lexicon JSON text, options and an `IExternalLexicons`, and
  returns files plus diagnostics. No `InternalsVisibleTo` is needed. The linked sources use only
  APIs available on `netstandard2.0` with the System.Text.Json package.
  *Source:* [02 — Architecture rules 3 and 5](../02-architecture.md#rules)
- **LEX-27** (MUST) `ATProtoNet.Lexicon.Generator` is an `IIncrementalGenerator` that packs CodeGen
  and its dependencies (System.Text.Json for `netstandard2.0` and its transitive packages) into
  `analyzers/dotnet/cs`. Phase 3 starts with a spike that proves this loads in `dotnet build`,
  the compiler server, Visual Studio and Rider. If the spike fails, the fallback is an MSBuild
  task package (`ATProtoNet.Lexicon.Build`) that runs the same emitter before `CoreCompile` with
  incremental `Inputs`/`Outputs`, the way Grpc.Tools runs protoc. First-party generation (CLI,
  checked in) is unaffected either way.
  *Source:* Decision
- **LEX-28** (MUST) First-party generated code is checked in under each package's `Generated/`
  folder. CI regenerates it and fails on any difference (`git diff --exit-code`).
  *Source:* [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json) ·
  [04 — Testing](../04-testing.md#lexicon-pipeline-tests)
- **LEX-29** (MUST) Generated files start with `// <auto-generated/>`, the source NSID and the
  manifest pin (source + CID), carry `#nullable enable`, and suppress only `CS0618` (generated code
  referencing its own `[Obsolete]` members). Output is deterministic: no timestamps, stable
  ordering by NSID, def name and property declaration order.
  *Source:* Decision
- **LEX-30** (MUST) Every generated assembly emits one `{Name}Lexicons` static class and
  `[assembly: LexiconAssembly(typeof({Name}Lexicons))]`. The class exposes the assembly's
  `IJsonTypeInfoResolver`, a `Register(LexiconTypeRegistry)` method that adds its records and
  union-extension variants, and the NSID → type table that external generators read.
  *Source:* Decision (replaces 0.8 `ILexiconPlugin` and the global registry)

### Configuration

- **LEX-31** (MUST) Generation options come from `lexgen.json` (CLI and generator) or, for the
  source generator only, MSBuild properties. Options are deliberately few:

  | Option | Meaning | Default |
  |---|---|---|
  | `rootNamespace` | namespace root (D-05) | `ATProtoNet.Lexicons` for first-party; project `RootNamespace` + `.Lexicons` for users |
  | `include` / `exclude` | NSID glob patterns | all / `internal.**` |
  | `accessors` | NSID authority prefix → client property name, or `@root` for members on `AtProtoClient` itself | derived (LEX-36) |
  | `renames` | `nsid#def` (or `nsid#def.property`) → C# name | none |
  | `proxies` | NSID glob → default `XrpcServiceRef`, with exclusions | none |
  | `experimental` | NSID glob → diagnostic ID | none |
  | `unionExtensions` | `nsid#def` → list of open unions in referenced assemblies it joins | none |
  | `visibility` | `public` or `internal` | `public` |

  First-party configuration sets `accessors` (`com.atproto` → `@root`, `app.bsky` → `Bsky`,
  `chat.bsky` → `Chat`, `tools.ozone` → `Ozone`, `site.standard` → `StandardSite`,
  `com.atproto.space`/`simplespace` → `@root` in `ATProtoNet.Spaces`), `proxies` (`chat.bsky.**`
  except `chat.bsky.moderation.**` → `did:web:api.bsky.chat#bsky_chat`), `experimental`
  (`app.bsky.unspecced.**` → `ATPROTO002`) and `exclude`
  (`app.bsky.notification.getGroupedNotifications`, flagged unstable upstream).
  *Source:* 0.8 `ConvoClient.cs:16` and siblings (chat proxy), `Upstream/README.md`
  (getGroupedNotifications) · [D-16](../06-decisions.md#d-16-experimental-diagnostic-ids) ·
  [Installing Lexicons: `.internal`](https://atproto.com/guides/installing-lexicons)

### Naming

<a id="naming"></a>

- **LEX-32** (MUST) Namespace: `{rootNamespace}.{every NSID segment except the last, PascalCased}`.
  `app.bsky.feed.post` → `ATProtoNet.Lexicons.App.Bsky.Feed`. PascalCasing upper-cases the first
  letter of each segment and keeps the rest (`atproto` → `Atproto` would be wrong: segments are
  split at case boundaries only when the segment is all lower-case and listed in a small table of
  protocol words, `atproto` → `AtProto`, `bsky` → `Bsky`).
  *Source:* [D-05](../06-decisions.md#d-05-lexicon-generated-type-naming) · 0.8 #45 ("`atproto`
  cased as `AtProto`")
- **LEX-33** (MUST) Type names: the `main` def takes the PascalCased last NSID segment (`Post`).
  Other defs of a non-`defs` document are nested in the main type (`Post.ReplyRef`); if the
  document's `main` is a query or procedure, they are nested in its static class. Defs of a
  `defs` document are top-level types in the namespace (`app.bsky.feed.defs#postView` →
  `PostView`). Queries, procedures and subscriptions become a static class named after the method
  (`GetTimeline`) with nested `Parameters`, `Input`, `Output`, `Message` and `Errors`.
  *Source:* [D-05](../06-decisions.md#d-05-lexicon-generated-type-naming)
- **LEX-34** (MUST) Inline schemas get deterministic nested names: an inline object property
  `foo` → nested type `FooObject`; inline `knownValues`/`enum` on property `foo` → nested struct
  `FooValue`; a union at property `foo` → a namespace-level interface
  `I{OwnerPath}{Foo}`, where `OwnerPath` concatenates the enclosing generated type names while
  omitting `Parameters`, `Input`, `Output` and `Message` (`Post.embed` → `IPostEmbed`,
  `GetPostThread.Output.thread` → `IGetPostThreadThread`); array items use the array property's
  name. The unknown variant of union `IXxx` is `UnknownXxx`. Token defs of a document become
  `public const string` members of a static class `{MainName}Tokens` (`Tokens` for a `defs`
  document), grouping token families in one class.
  *Source:* Decision · 0.8 `Emit_InlineObjectSchema_GeneratesNestedClass`,
  `Emit_TokenFamilies_GroupsConstantsIntoOneStaticClass` (#45)
- **LEX-35** (MUST) Member names: JSON keys are converted to legal C# identifiers by splitting on
  non-alphanumeric characters and PascalCasing the parts (`name:lang` → `NameLang`); a leading
  digit gets a `_` prefix; a member whose name equals its enclosing type gets a `Value` suffix.
  The `[JsonPropertyName]` always keeps the exact key.
  *Source:* 0.8 #45 (CS0542) · `Emit_PropertyNamesThatAreKeywordsOrDigits_ProducesLegalIdentifiers`,
  `Emit_BlobPropertyNamedLikeEnclosingType_RenamesMember`
- **LEX-36** (MUST) Namespace-client types: each NSID group gets a `readonly partial struct
  {LastGroupSegment}Namespace` in its namespace (`RepoNamespace`, `FeedNamespace`,
  `BskyNamespace`). The accessor of an authority prefix not listed in `accessors` is the
  PascalCased second-level domain segment of the prefix (`com.example` → `Example`).
  *Source:* [D-13](../06-decisions.md#d-13-lexicon-namespace-access-through-extension-properties)
- **LEX-37** (MUST) Collisions are generator errors with the two colliding references and the
  `renames` key that would resolve them. They are never resolved silently by suffixing. This
  includes a type named like its containing namespace's last segment
  (`tools.ozone.set.defs#set` → type `Set` in namespace `…Ozone.Set`), which the .NET naming
  guidelines forbid.
  *Source:* [D-05](../06-decisions.md#d-05-lexicon-generated-type-naming) ·
  [.NET namespace naming](https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-namespaces)

### Generated output

<a id="generated-clients"></a>

#### Models

- **LEX-38** (MUST) One generated type per def. Two defs with the same shape are still two types.
  *Source:* Decision · 0.8 merged shapes (`FeedResponse` serves 5 methods, `ConvoIdRequest` 10)
  that needed 86 `DefOf` drift mappings
- **LEX-39** (MUST) Object defs, records, `Input`/`Output`/`Message` objects become
  `sealed partial record` classes implementing `ILexiconObject`, with `init` properties.
  Lexicon-required properties are `required`; optional properties are nullable.
  *Source:* [AG-7](../03-api-guidelines.md#types) · 0.8 CONTRIBUTING "Lexicon models and clients"
- **LEX-40** (MUST) Lexicon `nullable`: a required, nullable property is a nullable C# property
  that is always written (JSON `null` when null). A non-required, nullable property is an
  `Optional<T?>` that distinguishes absent (not written) from `null` (written as `null`). Upstream
  has three sites: `com.atproto.repo.putRecord#input.swapRecord` (`Optional<Cid?>`),
  `com.atproto.sync.subscribeRepos#commit.since` and `#repoOp.cid` (always written).
  *Source:* [Lexicon spec: object](https://atproto.com/specs/lexicon) · Lexicon survey (3 sites)
- **LEX-41** (MUST) Every property carries an explicit `[JsonPropertyName]` with the exact Lexicon
  key; no naming policy is used.
  *Source:* 0.8 `Conventions/LexiconJsonNameTests`
- **LEX-42** (MUST) Unknown properties round-trip at every nesting level through a
  `[JsonExtensionData]` property (`ExtensionData`), which is `null` when there are none. `$type` is
  never captured as an unknown property. This includes fields upstream has deprecated.
  *Source:* 0.8 #115 · `UnknownFieldTests.PostRecord_UnknownFieldsAtEveryLevel_RoundTrip`,
  `…TypeIsNeverCapturedAsAnUnknownField`, `AtProtoRecord_WithoutUnknownFields_HasNoExtensionData`,
  `ProfileRecord_AllUpstreamFieldsAndUnknownOnes_RoundTrip`
- **LEX-43** (MUST) Type mapping:

  | Lexicon | C# |
  |---|---|
  | `boolean` | `bool` |
  | `integer` | `int` when `minimum` and `maximum` are both present and inside `Int32`, else `long` ([D-19](../06-decisions.md#d-19-lexicon-integer-maps-to-int-when-the-bounds-fit-otherwise-long)) |
  | `string` (no format) | `string` |
  | `string` format `did`, `handle`, `at-identifier`, `at-uri`, `nsid`, `cid`, `record-key`, `tid`, `datetime`, `language` | `Did`, `Handle`, `AtIdentifier`, `AtUri`, `Nsid`, `Cid`, `RecordKey`, `Tid`, `AtDatetime`, `Language` |
  | `string` format `uri` | `string` (keeps the exact text; `System.Uri` normalises) |
  | other formats | the type a referenced assembly declares with `[LexiconFormatType]` (`space-ref` → `SpaceUri`), else `string` with a warning |
  | `string` with `knownValues` or `enum` | known-values struct (LEX-47) |
  | `bytes` | `ReadOnlyMemory<byte>` (`{"$bytes": …}`) |
  | `cid-link` | `CidLink` (`{"$link": …}`) |
  | `blob` | `BlobRef` |
  | `array` | `IReadOnlyList<T>` |
  | `unknown` | `DataModelValue` |
  | `ref` | the target's type |
  | `union` | union interface (LEX-49) |

  *Source:* 0.8 CONTRIBUTING (typed identifiers), `TypedIdentifierGuardTests` · 0.8 #211
  (`space-ref` → `SpaceUri`) · [D-19](../06-decisions.md#d-19-lexicon-integer-maps-to-int-when-the-bounds-fit-otherwise-long)
- **LEX-44** (MUST) Identifier-typed properties are strict on read (an invalid value fails with
  `JsonException` carrying the JSON path and an inner `FormatException`); `datetime` is lenient on
  read (original text kept, `IsValid` reports parseability) and refuses to write a `default`.
  Reads are case-sensitive and numbers are not read from strings.
  *Source:* [D-20](../06-decisions.md#d-20-json-reads-are-case-sensitive-and-strict-about-number-types) ·
  0.8 `IdentityJsonConverters.cs:43-66`
- **LEX-45** (MUST) Null optional properties are omitted on write. Lexicon `default` values are
  documented in remarks but never emitted as C# initializers, so the client never sends a value
  the server would apply anyway.
  *Source:* 0.8 `AtProtoJsonDefaults` (`WhenWritingNull`) · Decision
- **LEX-46** (MUST) XML docs: `<summary>` from the def or property `description` (XML-escaped), or
  a generated sentence naming the NSID when there is none, so CS1591 never fires; constraints
  (`maxLength`, `maxGraphemes`, bounds, `default`, `accept`, `maxSize`) in `<remarks>`. A def,
  property or known value whose description starts with "Deprecated" (any case) gets
  `[Obsolete]` with that description.
  *Source:* [AG-35](../03-api-guidelines.md#stability-and-documentation) · 0.8 knownValues
  deprecations
- **LEX-47** (MUST) A string with `knownValues` (open) or `enum` (closed) becomes a `readonly
  partial record struct` over `string` implementing `IKnownValue<TSelf>`: static properties for
  each value (token refs use the token's name), `IsKnown`, implicit conversions to and from
  `string` (they cannot fail), and a generated converter. Any string round-trips. A `default`
  value (null `Value`) is not serializable. For `enum`, membership is enforced by
  `LexiconValidator`, not by deserialization.
  *Source:* [AG-9](../03-api-guidelines.md#types) · 0.8 `KnownValueConstants_MatchUpstream`;
  0.8 `RepoOpAction` was a closed C# enum for an open string (`SyncModels.cs:295`)
- **LEX-48** (MUST) Named string defs with `knownValues` (`com.atproto.moderation.defs#reasonType`)
  produce one struct referenced by every property that refs them.
  *Source:* Decision

#### Unions

- **LEX-49** (MUST) A union is a generated `partial interface` with `[JsonConverter]` pointing at a
  generated converter. Each variant type implements every union interface it belongs to, across
  packages when the referencing package is downstream (an Ozone union containing a `chat.bsky`
  type is declared in `ATProtoNet.Ozone`; the variant type is in `ATProtoNet.Bluesky`, so
  CodeGen emits a wrapper-free registration: the Ozone converter dispatches to the Bluesky type
  and the interface is satisfied by a generated `partial` declaration only within one assembly;
  across assemblies the converter returns the variant through a generated adapter record that
  implements the interface and exposes the variant as `Value`).
  *Source:* [D-18](../06-decisions.md#d-18-unions-are-interfaces) · Lexicon reference DAG
  (`com.atproto` ← `app.bsky` ← `chat.bsky` ← `tools.ozone`)
- **LEX-50** (MUST) Open unions get an `Unknown…` variant implementing `IUnknownUnionVariant`
  (`Type` = the `$type`, `Raw` = a detached copy of the object) that writes back its raw JSON
  unchanged. Closed unions have no unknown variant.
  *Source:* [D-18](../06-decisions.md#d-18-unions-are-interfaces) · 0.8
  `OpenUnionTests.Deserialize_UnknownVariant_ReadsAsUnknownAndWritesBackByteForByte`,
  `UnknownVariant_Constructor_DetachesRawFromTheCallersDocument`
- **LEX-51** (MUST) Union read: find `$type` anywhere in the object (not only first); match escaped
  discriminators unescaped; dispatch to the known variant; for open unions, consult the options'
  `LexiconTypeRegistry` and then fall back to `Unknown…`; for closed unions, throw `JsonException`
  on an unlisted `$type`. A member that is not an object or has no `$type` throws; JSON `null`
  reads as `null`.
  *Source:* 0.8 #50 · `OutOfOrderTypeMetadataTests`, `OpenUnionTests.Deserialize_EscapedDiscriminatorValue_IsMatchedUnescaped`,
  `…UnknownVariantOfClosedUnion_Throws`, `…UnionMemberWithoutType_Throws`,
  `…UnionMemberThatIsNotAnObject_Throws`, `…NullUnionMember_ReadsAsNull`, `CustomUnionTests`
- **LEX-52** (MUST) One unknown variant never fails its container: a page with one unrecognised
  embed or thread node deserializes, with that member as `Unknown…`.
  *Source:* 0.8 #115 · `Deserialize_UnknownEmbedInsidePostView_KeepsTheRestOfThePage`,
  `Deserialize_UnknownThreadNodeAmongReplies_KeepsTheKnownNodes`
- **LEX-53** (MUST) An invalid member inside a variant fails with the JSON path within the
  variant, and deeply nested unions fail after one deserialization attempt per level (no
  repeated re-reading). A variant converter cannot read past its value or stop short of it.
  *Source:* `Deserialize_InvalidMemberOfAVariant_NamesItsPathWithinTheVariant`,
  `…InvalidMemberUnderDeeplyNestedUnions_FailsFastWithTheErrorOfOneDeserialize`,
  `…VariantConverterThatReadsPastItsValue_CannotReachTheNextMember`
- **LEX-54** (MUST) Union write: the converter writes `$type` first (the NSID for a `main` def,
  `nsid#name` otherwise), then the variant's members. Writing a variant that is neither declared
  nor registered throws `NotSupportedException`. A variant serialized on its own, outside union
  context, writes no `$type`.
  *Source:* `OpenUnionTests.Serialize_KnownVariant_LeadsWithType…` ·
  `CustomUnionTests.Serialize_VariantThatIsNeitherDeclaredNorRegistered_Throws` ·
  [Lexicon spec: ref](https://atproto.com/specs/lexicon) (no `$type` on plain refs)
- **LEX-55** (MUST) `unionExtensions` lets a user def join an open union from a referenced
  assembly: the generated type implements that union's interface and `{Name}Lexicons.Register`
  adds it to a registry, so data with its `$type` reads as the typed variant.
  *Source:* 0.8 `LexiconTypeRegistry.RegisterUnionVariant` · `RegisteredVariant_OnSdkUnion_IsUsedByTheOptionsTheClientsUse`

#### Records

- **LEX-56** (MUST) A `record` def's type implements `IAtProtoRecord<TSelf>`: `Collection` is the
  NSID, `KeyType` comes from `key`, `JsonTypeInfo` is the generated contract. There is one record
  pattern; no record base class exists.
  *Source:* Decision · 0.8 had `AtProtoRecord` and `LexObject + IAtProtoRecord` side by side
  (`RecordCollection.cs:67`, `FeedModels.cs:16-22`)
- **LEX-57** (MUST) A record writes `$type` (= `Collection`) exactly once and first, and never a
  stray `type`. An incoming `$type` for another NSID is neither kept nor echoed.
  *Source:* 0.8 #49 · `RecordTypeDiscriminatorTests`, `UnknownFieldTests.AtProtoRecord_TypeFromAnotherNsid_IsNotKeptOrEchoed`
- **LEX-58** (MUST) A record whose Lexicon has an optional `createdAt` of format `datetime`
  implements `ICreatedAtRecord<TSelf>`; hand-written record types may implement it too. The
  records API stamps `createdAt` on create only through this interface and only when it is null,
  and never on put. A required `createdAt` is enforced by the compiler instead.
  *Source:* 0.8 `CreateAsync_WithoutCreatedAt_StampsTheCurrentTime`,
  `PutAsync_OfARecordReadWithoutCreatedAt_DoesNotAddOne` · Decision (interface, not base class)
- **LEX-59** (MUST) `DataModelValue.As<T>()` deserializes an `unknown` field (such as
  `PostView.record`) into a generated record through `T.JsonTypeInfo`, failing with
  `InvalidOperationException` when the value's `$type` is not `T.Collection`. `TryAs<T>` returns
  false instead. `AsKnownRecord(registry)` returns the record typed by its `$type`, or `null`.
  *Source:* Decision · 0.8 `PostView.Record` is a raw `JsonElement`

#### XRPC descriptors and parameters

- **LEX-60** (MUST) Each query, procedure and subscription static class has a `Descriptor`
  property of the descriptor type [xrpc-client.md](xrpc-client.md) defines:

  | Lexicon shape | Descriptor |
  |---|---|
  | query, JSON output | `XrpcQuery<Parameters, TOutput>` |
  | query, binary output (`application/vnd.ipld.car`, `application/jsonl`, `*/*`) | `XrpcQuery<Parameters, XrpcBinaryResponse>` |
  | procedure, JSON or no input, JSON output | `XrpcProcedure<TInput, TOutput>` |
  | procedure, no output | `XrpcProcedure<TInput>` |
  | procedure, binary input (`*/*`, `video/mp4`, CAR, octet-stream) | `XrpcProcedure<XrpcBinaryContent, TOutput>` |
  | subscription | `XrpcSubscription<Parameters, IMessage>` |

  `TOutput`/`TInput` is the nested `Output`/`Input` record for inline schemas or the referenced
  type for `ref` schemas. Methods declaring no parameters or no input use `XrpcEmpty`. The
  descriptor carries the NSID, encodings, declared error names, the default proxy from
  `proxies`, and the source-generated `JsonTypeInfo`s.
  *Source:* [D-12](../06-decisions.md#d-12-generated-xrpc-methods-take-one-parameters-record) ·
  Lexicon survey (267 JSON outputs, 48 procedures without output, 6 binary encodings)
- **LEX-61** (MUST) Whether a procedure has output comes from the Lexicon: a procedure without
  `output` never parses the response body, and an empty 200 is success. No hand-maintained list
  exists.
  *Source:* 0.8 #69 · `EmptyResponseBodyTests` (list "maintained by hand", `EmptyResponseBodyTests.cs:39-60`)
- **LEX-62** (MUST) A procedure that declares a JSON input whose properties are all optional sends
  `{}` when called without arguments; a procedure that declares no input sends no body.
  *Source:* 0.8 #123 · `WireFidelityTests.DeactivateAccountAsync_NoArguments_SendsAnEmptyJsonBody`,
  `UpdateAllReadAsync_NoStatus_SendsAnEmptyJsonBody`
- **LEX-63** (MUST) `Parameters` records implement `IXrpcParameters<TSelf>` with a generated
  writer (declaration order; arrays as repeated keys; invariant formatting as
  [xrpc-client.md](xrpc-client.md) specifies) and a generated reader the server uses for binding.
  No reflection over parameters.
  *Source:* 0.8 #123 · `WireFidelityTests.SearchPostsAsync_Tags_SendsOneTagParameterEach`,
  `GetListsAsync_Purposes_SendsOnePurposesParameterEach`
- **LEX-64** (MUST) Each method class has a nested `Errors` static class with one `const string`
  per declared error, so `ex.Is(GetRecord.Errors.RecordNotFound)` needs no magic strings.
  *Source:* Decision · 0.8 `XrpcErrors` had 28 names; upstream declares 286 errors
- **LEX-65** (MUST) The one upstream procedure with both parameters and binary input
  (`app.bsky.video.uploadPart`) is generated with the parameterized procedure descriptor that
  [xrpc-client.md](xrpc-client.md) provides for it.
  *Source:* Lexicon survey

#### Namespace clients

- **LEX-66** (MUST) Each `{X}Namespace` struct wraps an `AtProtoClient` and has one method per
  query and procedure in its group: `{Method}Async(Parameters parameters, CancellationToken
  cancellationToken = default)` for queries (`Parameters? parameters = null` when no parameter is
  required; no parameter argument when none are declared) and `{Method}Async(TInput input, …)` for
  procedures (`TInput? input = null` when all input fields are optional). Group structs expose
  child groups as properties (`BskyNamespace.Feed`).
  *Source:* [D-12](../06-decisions.md#d-12-generated-xrpc-methods-take-one-parameters-record),
  [AG-11–AG-13](../03-api-guidelines.md#methods-and-parameters)
- **LEX-67** (MUST) Accessors: for `@root` prefixes in the package that declares `AtProtoClient`,
  the generator emits instance properties into a `partial class AtProtoClient` part
  (`client.Repo`); everywhere else it emits C# 14 extension properties
  (`extension(AtProtoClient client) { public BskyNamespace Bsky => new(client); }`).
  *Source:* [D-13](../06-decisions.md#d-13-lexicon-namespace-access-through-extension-properties)
- **LEX-68** (MUST) Namespace clients expose no subscription methods. Subscriptions are consumed
  through `ATProtoNet.Streaming` with the generated `XrpcSubscription` descriptor
  ([streaming.md](streaming.md)) and served through [server.md](server.md).
  *Source:* [D-07](../06-decisions.md#d-07-one-consumption-model-for-every-event-stream)
- **LEX-69** (MUST) Types and members of NSIDs matched by `experimental` carry
  `[Experimental("ATPROTO0xx")]`.
  *Source:* [AG-33](../03-api-guidelines.md#stability-and-documentation), [D-16](../06-decisions.md#d-16-experimental-diagnostic-ids)

#### Pagination

- **LEX-70** (MUST) An output object with an optional string `cursor` property and exactly one
  array property implements `ICursorPage<TItem>`, with `Items` implemented explicitly over the
  array property so the wire shape is unchanged. Its `Parameters` (which has `cursor`)
  implements `ICursorParameters<TSelf>`.
  *Source:* [AG-17](../03-api-guidelines.md#pagination-and-streams) · 0.8 `Conventions/CursorPageTests`
  (78 cursored outputs upstream)
- **LEX-71** (MUST) For every such query the namespace struct also has
  `Enumerate{Name}Async(Parameters?, CancellationToken)` returning `IAsyncEnumerable<TItem>`,
  where `{Name}` is the method name without a leading `get` or `list` (`getFollowers` →
  `EnumerateFollowersAsync`, `searchPosts` → `EnumerateSearchPostsAsync`), implemented with the
  shared pagination helper.
  *Source:* [XRC-45](xrpc-client.md) · 0.8 kept 13 hand-written `Enumerate*`; generated, they cost
  nothing

#### Serialization contexts

- **LEX-72** (MUST) Each generated assembly has one source-generated `JsonSerializerContext`
  covering every generated type and every external type its descriptors and unions reach, so its
  descriptors work without a global resolver chain. Context options: no naming policy,
  case-sensitive, `JsonNumberHandling.Strict`, `WhenWritingNull`, `RespectNullableAnnotations`.
  *Source:* [D-10](../06-decisions.md#d-10-serialization-systemtextjson-with-source-generation-generated-union-converters),
  [D-20](../06-decisions.md#d-20-json-reads-are-case-sensitive-and-strict-about-number-types)
- **LEX-73** (MUST) Generated code is trim- and AOT-safe: no `Activator`, `ConstructorInfo.Invoke`,
  `MakeGenericType` or reflection-based contracts; union converters and known-value converters
  are generated per type.
  *Source:* [G6](../00-vision.md#goals) · 0.8 `AtProtoUnionConverter` builds shapes by reflection
- **LEX-74** (MUST) Generated types override `ToString()` (and suppress record member printing) to
  show only the type name and `$type`, because Lexicon cannot mark secrets and some outputs carry
  tokens (`com.atproto.server.createSession#output`).
  *Source:* [AG-29](../03-api-guidelines.md#security)

#### Permission sets and space declarations

- **LEX-75** (MUST) A `permission-set` def becomes a static class (`AuthFullApp`) with
  `Ref` (`PermissionSetRef`) and `Include(XrpcServiceRef? audience)`, which produces the
  `include:` scope string [auth.md](auth.md) parses.
  *Source:* 0.8 #130 (`Include(aud)`), `AtProtoScopes.PermissionSets` must match published sets
- **LEX-76** (MUST, experimental) A `space` def becomes a static class exposing the declaration as
  `ATProtoNet.Spaces.SpaceTypeDeclaration` when that assembly is referenced, otherwise a warning
  and no output.
  *Source:* 0.8 #92 · `Emit_SpaceDeclaration_*`

### Hand-written code on top of generated code

- **LEX-77** (MUST) Hand-written helpers live outside `Generated/`, as members of the generated
  `partial` types (`BskyNamespace.Posting.cs`) or as extension members. They never redeclare a
  generated type, member, JSON name or wire shape.
  *Source:* [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json)
- **LEX-78** (MUST) Helpers that need a dynamic proxy (feed interactions to
  `{feedGenerator}#bsky_fg`) use `client.With(…)`, not descriptor defaults.
  *Source:* 0.8 `FeedClient.cs:419`

### Users' own Lexicons

- **LEX-79** (MUST) A user adds `ATProtoNet.Lexicon.Generator` and lists schemas as
  `<AtProtoLexicon Include="lexicons/**/*.json" />`; the package's props turn these into
  `AdditionalFiles` with metadata the generator reads. An optional `lexgen.json` in the same
  items configures generation. Refs to first-party NSIDs resolve to the referenced SDK packages'
  types (LEX-8), so users vendor only their own schemas.
  *Source:* Decision
- **LEX-80** (MUST) Generated user code has the same shape as first-party code: records work with
  `client.Records<T>()`, methods hang off `client.{Accessor}`, unions and known values behave
  identically. Generator diagnostics have IDs `ATLEX001`–`ATLEX099` and point at the Lexicon file
  and JSON pointer.
  *Source:* Decision

### Runtime Lexicon resolution

- **LEX-81** (MUST, experimental `ATPROTO004`) `LexiconResolver` (namespace
  `ATProtoNet.LexiconResolution`, package `ATProtoNet`) resolves an NSID by querying the DNS TXT
  record `_lexicon.{authority segments reversed, without the name segment}` (not hierarchical),
  requiring exactly one `did=` value, resolving that DID, fetching
  `com.atproto.lexicon.schema/{nsid}` with `com.atproto.sync.getRecord` from its PDS, verifying the
  record proof against the DID's signing key, and requiring the schema `id` to equal the NSID.
  All fetches use the untrusted-fetch layer.
  *Source:* [Lexicon spec: publication and resolution](https://atproto.com/specs/lexicon) · 0.8
  `LexiconResolver.cs` · vector `lexicon-resolution.json` (@atproto/lex-resolver 0.3.0)
- **LEX-82** (MUST) Failures are `LexiconResolutionException`. The resolver caches nothing; a
  caching decorator with `InvalidateAsync(nsid)` wraps it. A service-backed resolver
  (`com.atproto.lexicon.resolveLexicon`) is a separate implementation that trusts its service.
  *Source:* 0.8 `ILexiconResolver`, `CachingLexiconResolver`, `LexiconClient`

## Limits

| Constant | Value | Applies to |
|---|---|---|
| `MaxLexiconDocumentBytes` | 1 MiB | parsing files and resolved schema records |
| `MaxDefinitionsPerDocument` | 1,024 | parsing |
| `MaxSchemaNestingDepth` | 32 | inline nesting within one def |
| `MaxValidationDepth` | 64 | data validation recursion (matches the DRISL depth limit) |
| `MaxValidationErrors` | 100 | violations collected before validation stops |
| `MaxRegistryVariantsPerUnion` | 256 | `LexiconTypeRegistry` additions per union |

The union converter searches for `$type` only within the current object, which the XRPC
success-body cap already bounds ([xrpc-client.md](xrpc-client.md)).

## Errors

| Failure | Result |
|---|---|
| Malformed Lexicon document | `FormatException` from `Parse`; diagnostics from `TryParse` |
| Lint findings, unresolved refs, collisions | `LexiconDiagnostic` (rule ID, severity, pointer); generator diagnostics `ATLEX0xx` |
| Data violates a schema | `LexiconValidationResult` with paths; `LexiconValidationException` where an API must throw |
| JSON does not fit a generated type (missing required, wrong type, invalid identifier, unknown variant of a closed union) | `JsonException` with JSON path (wrapped by the XRPC layer as `XrpcResponseFormatException`) |
| Writing an undeclared, unregistered union variant | `NotSupportedException` |
| `As<T>()` on a value of another `$type` | `InvalidOperationException` |
| Runtime resolution failure | `LexiconResolutionException : AtProtoException` |

## Conformance and tests

- **Vectors:** interop-tests `lexicon/` suites (newly vendored: schema validity and data
  validation), `lexicon-resolution.json`, the golden XRPC wire fixtures (run against generated
  clients).
- **Generator:** all vendored Lexicons generate and compile with zero diagnostics (extends #141,
  `CSharpEmitterCompileTests`); golden emitter output for a representative set (record, paginated
  query, procedure with binary input, open union, closed union, known values, nested defs,
  permission set, space def); CLI and source-generator output are byte-identical; CI
  regeneration check.
- **Scenarios to port:** `OpenUnionTests`, `OutOfOrderTypeMetadataTests`,
  `RecordTypeDiscriminatorTests`, `UnknownFieldTests`, `CustomUnionTests`, `LexiconUnionTests`,
  `WireFidelityTests`, `EmptyResponseBodyTests` (now driven by Lexicon data),
  `CSharpEmitterTests` (naming and collision cases), `LexiconLinterTests`, `LexiconDifferTests`,
  `LexiconResolverTests`, the record parts of `RecordCollectionTests`.
- **Retired** (generation makes them redundant): `LexiconDriftTests`, `SdkSource`,
  `TypedIdentifierGuardTests`, `CursorPageTests`, `LexiconJsonNameTests`, the `SdkTypeMap` tests.

## Changes from 0.8

| Change | Reason |
|---|---|
| All Lexicon models and clients are generated; 100% of vendored methods are covered (0.8: 279 of 321) | D-04 |
| One type per def; merged shapes (`FeedResponse`, `ConvoIdRequest`) and their names disappear | LEX-38: generated code cannot share shapes without heuristics |
| Unions are interfaces with generated converters; no `Unknown*` hand classes, no duplicate embed types | D-18 |
| A union variant serialized outside union context writes no `$type` (0.8 always wrote it) | Lexicon spec: no `$type` on plain refs; records and union members are unaffected |
| No `AtProtoRecord` base class or #49 contract modifier; `createdAt` stamping via `ICreatedAtRecord<TSelf>` | One record pattern |
| `integer` maps by the D-19 rule (0.8: `int` 189×, `long` 33×, no rule) | D-19 |
| Known values are open structs; closed C# enums for open strings (`RepoOpAction`) are gone | AG-9 |
| Methods take one parameters or input record; per-call options go through `client.With(…)` | D-12 |
| Case-sensitive reads, no numbers from strings | D-20 |
| `language` maps to `Language` (0.8: `string`) | Typed identifiers everywhere ([syntax.md](syntax.md)) |
| Non-required `nullable` fields are `Optional<T?>`; 0.8 could not send `swapRecord: null` | LEX-40 |
| `LexiconTypeRegistry` is an instance; `ILexiconPlugin` is replaced by generated `Register` | D-10 |
| Diff treats loosening a constraint and making a field optional as breaking (0.8: compatible) | Lexicon versioning guide |
| Unresolvable refs are errors (0.8: `JsonElement?` with a warning) | LEX-6 |
| Generated `ToString()` prints no members | AG-29 |

## Open points

| # | Question | Proposed default |
|---|---|---|
| LEX-O1 | Align `lexicons.json` exactly with `@atproto/lex`'s manifest (`version`, `lexicons`, `resolvers`, `resolutions`) so one manifest serves several SDKs? | Yes where the formats overlap; our extra fields (CID-in-record-form, provenance) go under an `x-atprotonet` key. Verify against `@atproto/lex-installer` at phase 3 start. |
| LEX-O2 | Closed `enum` as an open struct (LEX-47) or a C# `enum`? | Open struct, lenient read, validator-enforced; a C# enum breaks readers when upstream breaks the rule. |
| LEX-O3 | Exact extension-data property type (`IDictionary<string, JsonElement>` vs `JsonObject`) under STJ source generation with `init`. | Whichever STJ source generation supports with `init` and no reflection; decided in the phase 3 spike. |
| LEX-O4 | Cross-assembly union membership (LEX-49): adapter record vs requiring every union's variants to be in the same or a downstream assembly. | Downstream packages declare the interface and emit `partial` implementations only for their own types; upstream variants are wrapped by a generated adapter. Revisit if the adapter proves awkward in Ozone. |
| LEX-O5 | Generate `Enumerate*Async` for every paginated query (LEX-71) or only listed ones? | Every one. |
| LEX-O6 | Direct DRISL-CBOR binding for generated types (skipping `DataModelValue`)? | Later optimisation behind the same API (D-10). |
| LEX-O7 | Should `app.bsky.unspecced.*` be generated at all? | Yes, marked `ATPROTO002`. |
