# Lexicon: schema model, validation, code generation

**Status:** Accepted (maintainer, 2026-10-17) · **Package:** `ATProtoNet` — namespaces `ATProtoNet.Lexicon` (schema model,
catalog, lint and compatibility rules, validation), `ATProtoNet.Lexicon.Runtime` (generated-code
runtime), `ATProtoNet.LexiconResolution` (runtime resolution), and the generated
`ATProtoNet.Lexicons.*` families checked in under `src/ATProtoNet/Generated/`. Internal build
projects: `ATProtoNet.Lexicon.CodeGen` (emitter, `netstandard2.0`) and
`ATProtoNet.Lexicon.Generator` (Roslyn), which ships as an analyzer inside the `ATProtoNet`
package. Runtime code targets `net11.0` with C# 15; the generator projects target
`netstandard2.0` but emit C# 15 ([D-25](../06-decisions.md)) · **Phase:** 3 (model, validation, CodeGen, first-party `com.atproto` generation including the
`com.atproto.space`/`simplespace` families), 6
(runtime resolution), 8 (bundled source generator, the remaining Lexicon families) ·
**Normative references:** [Lexicon](https://atproto.com/specs/lexicon),
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
    public ReadOnlyMemory<byte> SourceUtf8 { get; }       // exact bytes parsed, for publishing
    public static LexiconDocument Parse(ReadOnlySpan<byte> utf8Json);            // FormatException
    public static bool TryParse(ReadOnlySpan<byte> utf8Json, out LexiconDocument? document,
        out IReadOnlyList<LexiconDiagnostic> diagnostics);
}

public abstract record LexSchema
{
    public string? Description { get; init; }
    public JsonElement Raw { get; init; }
}
public abstract record LexUserType : LexSchema;           // what may appear under "defs"
public sealed record LexRecord : LexUserType
{
    public required RecordKeyType Key { get; init; }
    public required LexObject Record { get; init; }
}
public sealed record LexQuery : LexUserType               // LexProcedure adds Input; LexSubscription has Message
{
    public LexParams? Parameters { get; init; }
    public LexBody? Output { get; init; }
    public IReadOnlyList<LexError> Errors { get; init; } = [];
}
public sealed record LexObject : LexUserType
{
    public required IReadOnlyDictionary<string, LexSchema> Properties { get; init; }
    public IReadOnlyList<string> Required { get; init; } = [];
    public IReadOnlyList<string> Nullable { get; init; } = [];
}
// LexProcedure, LexSubscription, LexPermissionSet, LexSpace, LexToken,
// LexString (format, length and grapheme bounds, enum, const, default, knownValues),
// LexInteger, LexBoolean, LexBytes, LexCidLink, LexBlob, LexArray, LexUnknown, LexRef, LexUnion
// (the last three accepted as named defs with a warning, LEX-3), LexParams, LexBody(Encoding, Schema),
// LexError(Name, Description)

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
public interface ILexiconType<TSelf> : ILexiconObject where TSelf : ILexiconType<TSelf>
{
    static abstract string LexiconType { get; }          // "app.bsky.embed.images", "nsid#def": the $type in union position
    static abstract JsonTypeInfo<TSelf> JsonTypeInfo { get; }
}
public interface IAtProtoRecord<TSelf> : IAtProtoRecord, ILexiconType<TSelf> where TSelf : IAtProtoRecord<TSelf>;
public interface ICreatedAtRecord<TSelf> where TSelf : ICreatedAtRecord<TSelf>
{
    AtDatetime? CreatedAt { get; }
    static abstract TSelf WithCreatedAt(TSelf record, AtDatetime createdAt);
}
// The extra case of every open union (LEX-50). One shared type: a case type needs no per-union
// declaration, because C# 15 unions take any types as cases (D-18).
public sealed record LexiconUnknown
{
    public string Type { get; }                           // the $type as received
    public JsonElement Raw { get; }                       // detached copy, written back unchanged
    public static LexiconUnknown From<T>(T value) where T : class, ILexiconType<T>;  // user extension variants (LEX-86)
    public bool TryAs<T>([NotNullWhen(true)] out T? value) where T : class, ILexiconType<T>;
}
// Base of the generated union converters: buffering, $type search, case dispatch and $type
// insertion on write; each generated subclass supplies only its case table (LEX-51, LEX-54).
public abstract class LexiconUnionConverter<TUnion> : JsonConverter<TUnion>
    where TUnion : struct, System.Runtime.CompilerServices.IUnion;
public interface IKnownValue<TSelf> where TSelf : struct, IKnownValue<TSelf>
{
    string Value { get; }
    bool IsKnown { get; }
    static abstract TSelf Create(string value);
}
public interface ICursorPage<T> { string? Cursor { get; } IReadOnlyList<T> Items { get; } }
public interface ICursorParameters<TSelf> where TSelf : ICursorParameters<TSelf> { string? Cursor { get; init; } }
public readonly record struct Optional<T>;                // absent vs present (possibly null): Lexicon `nullable` + not required
// PermissionSetRef lives in ATProtoNet.Auth (it formats scopes with XrpcServiceRef); see auth.md.

public sealed class LexiconTypeRegistry                   // an instance, never a global (D-10); records only
{
    public void AddRecord<TRecord>() where TRecord : class, IAtProtoRecord<TRecord>;
    public bool TryGetRecordType(Nsid collection, out JsonTypeInfo typeInfo);
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]   // generated unions are structs
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

- **LEX-1** (MUST) There is exactly one Lexicon schema object model, in the `ATProtoNet.Lexicon`
  namespace. The
  runtime (validation, resolution), CodeGen, the CLI and the tests all use it. No second
  representation of Lexicon documents, permission sets or defs exists anywhere in the codebase.
  *Source:* Decision · 0.8 has three overlapping models (`tools/…/Schema/LexiconDocument.cs`, the
  runtime `LexiconSchemaRecord` with `Defs` as raw `JsonElement` at `LexiconModels.cs:44`, and the
  tests' `UpstreamLexicons`), plus `AtProtoScopes.PermissionSets` and `SdkTypeMap` as mirrors
- **LEX-2** (MUST) The model is immutable and typed: one record type per Lexicon type (`record`,
  `query`, `procedure`, `subscription`, `permission-set`, `token`, `object`, `string`,
  `integer`, `boolean`, `bytes`, `cid-link`, `blob`, `array`, `unknown`, `ref`, `union`,
  `params`, `space`). Every node keeps its source
  `JsonElement`, and every document keeps its exact source bytes, so publishing never
  re-serializes (CIDs come from the DRISL encoding, LEX-23).
  *Source:* [Lexicon spec](https://atproto.com/specs/lexicon) · Decision · 0.8 #92 (space defs)
- **LEX-3** (MUST) Parsing enforces the document shape: `lexicon` is `1`; `id` is a valid NSID; at
  least one def; at most one primary type (`record`, `query`, `procedure`, `subscription`,
  `permission-set`, `space`), and only under the name `main`; `params` cannot be a named def;
  `params` properties are limited to `boolean`, `integer`, `string` and arrays of those.
  Violations are `LexiconDiagnostic`s with a JSON pointer; `Parse` throws `FormatException`
  naming the first one. Named `ref`, `union` and `unknown` defs, which the spec does not allow but
  published schemas use, are accepted with a lint warning: a named `union` generates a union type
  named after the def, a named `ref` is an alias for its target, a named `unknown` maps to
  `DataModelValue`.
  *Source:* [Lexicon spec: Lexicon Files, params](https://atproto.com/specs/lexicon) · 0.8
  `LexiconLinter.cs:46-51` ("used by published schemas"),
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
- **LEX-7** (MUST) A `ref` to a `token` is an error. A `ref` that resolves to a named `ref` def is
  followed (alias chains up to `MaxRefAliasDepth`); a cycle of aliases is an error. A `union`
  member that resolves to a `union` is an error.
  *Source:* [Lexicon spec: ref](https://atproto.com/specs/lexicon) · LEX-3
- **LEX-8** (MUST) Definitions outside the catalog's documents resolve through
  `IExternalLexicons`. The generator and CLI implement it from `[LexiconDefinition]` attributes on
  types in referenced assemblies, `ATProtoNet` itself included (Roslyn symbols in the generator,
  `System.Reflection.Metadata` in the CLI, never loading the assembly). This replaces 0.8's hand-maintained `SdkTypeMap`.
  *Source:* Decision · 0.8 `tools/…/CodeGen/SdkTypeMap.cs` (≈45 hand-listed entries)
- **LEX-9** (MUST) Recursive structures through `ref`/`union` (thread views, nested replies) are
  supported. The catalog detects only cycles that cannot terminate (a required, non-nullable,
  non-array self-reference) and reports them.
  *Source:* Decision

### Lint rules

- **LEX-10** (MUST) Lint rules live in the `ATProtoNet.Lexicon` namespace (`LexiconLint`), with stable rule IDs
  (`LXL001`…), severity `Error` or `Warning`, a message and a JSON pointer. The CLI's `lint` and
  `publish` and the generator all run the same rules.
  *Source:* Decision · 0.8 `tools/…/Validation/LexiconLinter.cs`
- **LEX-11** (MUST) Permission-set rules: the set is `main`; it has a `title` and at least one
  permission; `blob`, `account` and `identity` permissions are errors (they must be requested as
  scopes directly); `repo` and `rpc` are allowed, each warning on parameters that do not apply to
  it; no wildcards; every NSID is in the set's own namespace group or below; repo actions are
  `create`, `update`, `delete`, each at most once; malformed permissions are errors; other
  resources are warnings that say authorization servers ignore them.
  *Source:* [Permission spec](https://atproto.com/specs/permission) · 0.8 #130 ·
  `LexiconLinterTests.Lint_*` (permission-set cases)
- **LEX-12** (MUST) Space rules: collections warn on wildcards and non-NSIDs, not on long names.
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
  `ATProtoNet.AspNetCore` validates inputs it serves ([server.md](server.md)).
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
  Spaces Lexicons `com.atproto.space.*` and `com.atproto.simplespace.*` from the permissioned-data
  proposal repository at the commit pinned by [spaces.md](spaces.md) (SPC-1) until the GA Lexicons
  are published in `bluesky-social/atproto`, then from there like the rest of `com.atproto.*`. They
  are ordinary first-party families: no experimental marker, same generation, same tests.
  *Source:* 0.8 `Lexicon/Upstream/README.md`, `refresh.sh` · [D-24](../06-decisions.md#d-24-spaces-scope-in-the-rewrite) ·
  [SPC-3](spaces.md#protocol-status-pins-and-placement)

### Generator architecture

<a id="generator-architecture"></a>

- **LEX-25** (MUST) One emitter, in `ATProtoNet.Lexicon.CodeGen` (`netstandard2.0`), produces all
  generated C#. The CLI's `generate` and the Roslyn generator both call it; for the same inputs
  and options their output is byte-identical (a golden test asserts this).
  *Source:* [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json)
- **LEX-26** (MUST) CodeGen compiles the schema-model sources of the `ATProtoNet.Lexicon`
  namespace (in the `ATProtoNet` project) as linked files (one source of truth, compiled twice).
  The linked sources declare their accessibility through a conditional symbol so they are
  `public` in `ATProtoNet` and `internal` in CodeGen; CodeGen's public API takes Lexicon JSON text, options and an `IExternalLexicons`, and
  returns files plus diagnostics. No `InternalsVisibleTo` is needed. The linked sources use only
  APIs available on `netstandard2.0` with the System.Text.Json package.
  *Source:* [02 — Architecture rules](../02-architecture.md#rules) (no `InternalsVisibleTo`
  outside tests; the generator has no runtime dependency on the SDK)
- **LEX-27** (MUST) `ATProtoNet.Lexicon.Generator` is an internal build project: an
  `IIncrementalGenerator` packed, together with CodeGen and its dependencies (System.Text.Json for
  `netstandard2.0` and its transitive packages), into `analyzers/dotnet/cs` of the **`ATProtoNet`
  package**. There is no separate generator package: referencing `ATProtoNet` is all a user
  needs. Phase 3 starts with a spike that proves this loads in `dotnet build`, the compiler
  server, Visual Studio and Rider. If the spike fails, the fallback is an MSBuild task, shipped in
  the same `ATProtoNet` package's `build/` targets, that runs the same emitter before
  `CoreCompile` with incremental `Inputs`/`Outputs`, the way Grpc.Tools runs protoc. First-party
  generation (CLI, checked in) is unaffected either way.
  *Source:* Decision
- **LEX-28** (MUST) First-party generated code is checked in under `src/ATProtoNet/Generated/`,
  one folder per Lexicon family. CI regenerates it and fails on any difference (`git diff --exit-code`), and a test
  generates every vendored Lexicon and compiles the result with Roslyn with zero diagnostics:
  assertions on emitted text alone let a CS0266 ship (#141).
  *Source:* [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json) ·
  [04 — Testing](../04-testing.md#lexicon-pipeline-tests) · 0.8 #141 · `CSharpEmitterCompileTests`
- **LEX-29** (MUST) Generated files start with `// <auto-generated/>`, the source NSID and the
  manifest pin (source + CID), carry `#nullable enable`, and suppress only `CS0618` (generated code
  referencing its own `[Obsolete]` members). Every type reference is `global::`-qualified, so no
  `using` directive is needed and namespace/type name clashes cannot break compilation. Output is
  deterministic: no timestamps, stable ordering by NSID, def name and property declaration order.
  *Source:* Decision · 0.8 #45 (missing `using`s, `global::` qualification)
- **LEX-30** (MUST) Every generation unit (each first-party Lexicon family inside `ATProtoNet`,
  or a user project) emits one `{Name}Lexicons` static class (`AtProtoLexicons`,
  `BlueskyLexicons`, …) and `[assembly: LexiconAssembly(typeof({Name}Lexicons))]`; an assembly
  may carry several. The class exposes the unit's `IJsonTypeInfoResolver`, a
  `Register(LexiconTypeRegistry)` method that adds its records, and the NSID → type table that
  external generators read. (Union extensions need no registration since the switch to C# 15
  unions; see LEX-86.)
  *Source:* Decision (replaces 0.8 `ILexiconPlugin` and the global registry)
- **LEX-83** (MUST) Emitted code is C# 15 and requires `net11.0` or later: it declares `union`
  types, which rely on `System.Runtime.CompilerServices.UnionAttribute` and `IUnion` from the
  .NET 11 runtime. The generator and CodeGen themselves stay `netstandard2.0` (a Roslyn
  requirement). In a user project that targets an older framework or sets `LangVersion` below 15,
  the source generator emits nothing and reports one error diagnostic (`ATLEX0xx`) naming the
  requirement, instead of producing code that fails to compile.
  *Source:* [D-25](../06-decisions.md) · [What's new in C# 15](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-15)

### Configuration

- **LEX-31** (MUST) Generation options come from `lexgen.json` (CLI and generator) or, for the
  source generator only, MSBuild properties. Options are deliberately few:

  | Option | Meaning | Default |
  |---|---|---|
  | `rootNamespace` | namespace root (D-05) | `ATProtoNet.Lexicons` for first-party; project `RootNamespace` + `.Lexicons` for users |
  | `include` / `exclude` | NSID glob patterns | all / `internal.**` |
  | `accessors` | NSID authority prefix → client property name, or `@root` for members on `AtProtoClient` itself | derived (LEX-36) |
  | `renames` | `nsid#def` (or `nsid#def.property`) → C# name | none |
  | `fieldTypes` | `nsid#def.property` → `decimal`: a plain `string` field that carries a number (LEX-92) | none |
  | `proxies` | NSID glob → default `XrpcServiceRef`, with exclusions | none |
  | `experimental` | NSID glob → diagnostic ID | none |
  | `addressFields` | `nsid#def.property` globs whose `at-uri` fields map to `AtAddress` (LEX-88) | none |
  | `unionExtensions` | `nsid#def` → open unions in referenced assemblies it extends; generates typed helpers (LEX-86) | none |
  | `visibility` | `public` or `internal` | `public` |

  First-party configuration sets `accessors` (`com.atproto` → `@root`, `app.bsky` → `Bsky`,
  `chat.bsky` → `Chat`, `tools.ozone` → `Ozone`, `site.standard` → `StandardSite`,
  `com.atproto.space` → `Space`, `com.atproto.simplespace` → `SimpleSpace`; the more specific
  entries win over `com.atproto` → `@root`, so the Spaces accessors are extension properties
  like every other family's and the client core stays free of family namespaces, LEX-67),
  `proxies` (`chat.bsky.**` except `chat.bsky.moderation.**` →
  `did:web:api.bsky.chat#bsky_chat`), `experimental` (`app.bsky.unspecced.**` → `ATPROTO002`
  only; the Spaces families are not listed, see LEX-87), `addressFields`
  (`com.atproto.space.**` and `com.atproto.simplespace.**`, plus the fields LEX-88 names) and `exclude`
  (`app.bsky.notification.getGroupedNotifications`, flagged unstable upstream).
  *Source:* 0.8 `ConvoClient.cs:16` and siblings (chat proxy), `Upstream/README.md`
  (getGroupedNotifications) · [D-16](../06-decisions.md#d-16-experimental-diagnostic-ids) ·
  [Installing Lexicons: `.internal`](https://atproto.com/guides/installing-lexicons)

### Naming

<a id="naming"></a>

- **LEX-32** (MUST) Namespace: `{rootNamespace}.{every NSID segment except the last, PascalCased}`.
  `app.bsky.feed.post` → `ATProtoNet.Lexicons.App.Bsky.Feed`. PascalCasing upper-cases the first
  letter of each segment and keeps the rest, except for a fixed table of protocol words with
  established casing (`atproto` → `AtProto`), so `com.atproto.repo` → `Com.AtProto.Repo`.
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
  `FooValue`; a union at property `foo` → a namespace-level union type `{OwnerPath}{Foo}`, where
  `OwnerPath` concatenates the enclosing generated type names while omitting `Parameters`,
  `Input`, `Output` and `Message` and dropping a method name's leading verb (`Get`, `List`,
  `Search`, `Query`, `Create`, `Put`, `Update`, `Delete`, `Describe`), and a word repeated across
  the join is written once (`Post.embed` → `PostEmbed`, `GetPostThread.Output.thread` →
  `PostThread`, not `GetPostThreadThread`); array items use the array property's name; a subscription's `message`
  union is `{Method}Message` (`SubscribeReposMessage`). Open unions share the `LexiconUnknown`
  case (LEX-50); no per-union unknown type exists. Token defs of a document become
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
- **LEX-37** (MUST) Two definitions that map to the same C# type name are a generator error naming
  both references and the `renames` key that would resolve them; type-name collisions are never
  resolved silently by suffixing (LEX-35's member renames are fixed rules, not collision
  handling). A type named like its namespace's last segment is a warning, not an error: upstream
  has five (`com.atproto.label.defs#label`, `app.bsky.bookmark.defs#bookmark`,
  `app.bsky.draft.defs#draft`, `tools.ozone.moderation.defs#moderation`,
  `tools.ozone.set.defs#set`), and renaming `Label` would hurt more than the ambiguity, which
  generated code avoids by qualifying every type name with `global::` (LEX-29).
  *Source:* [D-05](../06-decisions.md#d-05-lexicon-generated-type-naming) ·
  [.NET namespace naming](https://learn.microsoft.com/dotnet/standard/design-guidelines/names-of-namespaces) ·
  Lexicon survey
- **LEX-92** (MUST) **Numbers carried in strings are an explicit, per-field opt-in.** The data
  model has no floating-point type, so Lexicons that need fractions declare a plain `string` and
  write the number as text (`community.lexicon.location.geo` does this for `latitude`,
  `longitude` and `altitude`). Generated code types such a field as `string` unless the
  configuration's `fieldTypes` maps it to `decimal`, which generates
  [`DecimalString`](syntax.md#numbers-carried-in-strings) instead (for an array of strings, its
  items). The Lexicon JSON is not changed, so other implementations still see a string, and the
  wire value stays a JSON string in both directions. Mapping a field that is not a plain `string`
  (it has a `format`, `knownValues` or `enum`) is a generator error; an entry that matches no
  field is a warning. A JSON number in such a field is still a `JsonException` (D-20): the
  opt-in changes the C# type, not what the reader accepts.
  *Source:* Decision (maintainer, 2026-10-17: string-carried floats must stay possible, flagged
  explicitly by the developer) ·
  [D-20](../06-decisions.md#d-20-json-reads-are-case-sensitive-and-strict-about-number-types) ·
  lexicon.community `community.lexicon.location.geo`
- **LEX-89** (MUST) **Namespaces are mechanical, type names are friendly** (Q-8, maintainer
  2026-10-17). Every generated type name reads like a hand-written .NET type name: a PascalCase
  noun phrase (static classes for methods keep their verb, `GetTimeline`), at most 40 characters,
  with no protocol plumbing words (`Main`, `Defs`, `Def`), no word repeated back to back
  (`ThreadThread`), no stacked generated suffixes (`FooObjectValue`) and no leading digit or
  underscore. The generator checks every name it emits against these rules and reports a
  violation as a warning naming the definition and suggesting a `renames` entry; first-party
  generation runs with zero warnings, so every violation is fixed before code is checked in.
  *Source:* Decision (Q-8) · 0.8's hand-written names (`FeedViewPost`, `PostView`) as the bar
- **LEX-90** (MUST) The generator writes a **type-name inventory** next to the generated code
  (`Generated/TypeNames.txt`: one sorted line per definition, `nsid#def → Namespace.TypeName`).
  It is checked in, so every naming change from a Lexicon sync, a rule change or a rename shows
  up as a reviewable diff, alongside the public API baseline.
  *Source:* Decision (Q-8)
- **LEX-91** (MUST) First-party naming exceptions live in the first-party `lexgen.json`
  `renames` table, each entry with a one-line reason. The table is reviewed with the inventory in
  phase 3 and stays short: a rename is for a name that breaks LEX-89 or collides (LEX-37), not for
  taste. Users' configurations use the same mechanism.
  *Source:* Decision (Q-8)

### Generated output

<a id="generated-clients"></a>

#### Example output (non-normative)

The requirements below define this shape; names and details may change in review.

```csharp
// <auto-generated/> app.bsky.feed.post · atproto@a7c8604d · bafyrei…
#nullable enable
namespace ATProtoNet.Lexicons.App.Bsky.Feed;

/// <summary>Record containing a Bluesky post.</summary>
[LexiconDefinition("app.bsky.feed.post")]
public sealed partial record Post : IAtProtoRecord<Post>          // a record (LEX-56)
{
    public static Nsid Collection { get; } = Nsid.Parse("app.bsky.feed.post");
    public static string LexiconType => "app.bsky.feed.post";
    public static RecordKeyType KeyType => RecordKeyType.Tid;
    public static JsonTypeInfo<Post> JsonTypeInfo => BlueskyLexiconsJsonContext.Default.Post;

    /// <summary>The primary post content. May be an empty string, if there are embeds.</summary>
    /// <remarks>Lexicon: maxLength 3000 (UTF-8 bytes), maxGraphemes 300.</remarks>
    [JsonPropertyName("text")] public required string Text { get; init; }
    [JsonPropertyName("reply")] public ReplyRef? Reply { get; init; }
    // Optional unions are the union struct itself; default (no active case) = absent (LEX-84).
    [JsonPropertyName("embed"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PostEmbed Embed { get; init; }
    [JsonPropertyName("labels"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public PostLabels Labels { get; init; }
    [JsonPropertyName("langs")] public IReadOnlyList<Language>? Langs { get; init; }
    [JsonPropertyName("createdAt")] public required AtDatetime CreatedAt { get; init; }
    [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    [LexiconDefinition("app.bsky.feed.post#replyRef")]
    public sealed partial record ReplyRef : ILexiconType<ReplyRef>
    {
        public static string LexiconType => "app.bsky.feed.post#replyRef";
        public static JsonTypeInfo<ReplyRef> JsonTypeInfo => BlueskyLexiconsJsonContext.Default.PostReplyRef;
        [JsonPropertyName("root")] public required StrongRef Root { get; init; }
        [JsonPropertyName("parent")] public required StrongRef Parent { get; init; }
        [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
    }
}

// An open union (LEX-49–LEX-54): the five embed kinds plus the shared unknown case. The case
// types implement nothing union-specific; Record is also a case of chat message embeds. (Type
// names abbreviated here; generated code is global::-qualified, LEX-29.)
[LexiconDefinition("app.bsky.feed.post#embed")]
[JsonConverter(typeof(PostEmbedConverter))]
public union PostEmbed(Embed.Images, Embed.Video, Embed.External, Embed.Record, Embed.RecordWithMedia, LexiconUnknown);

internal sealed class PostEmbedConverter : LexiconUnionConverter<PostEmbed> { /* generated $type → case table */ }

// Usage (LEX-85): implicit conversion in, type patterns out.
//   var post = new Post { Text = "hi", CreatedAt = AtDatetime.Now(), Embed = new Embed.Images { … } };
//   string kind = post.Embed switch
//   {
//       Embed.Images => "images", Embed.Video => "video", Embed.External => "link",
//       Embed.Record => "quote", Embed.RecordWithMedia => "quote with media",
//       LexiconUnknown unknown => unknown.Type,
//       null => "none",                           // the null pattern tests for no active case
//   };

// A paginated query (LEX-60, LEX-63, LEX-70) with inline known values (LEX-47).
public static partial class GetAuthorFeed
{
    public static XrpcQuery<Parameters, Output> Descriptor { get; } = /* NSID, encodings, errors, type infos */;

    public sealed partial record Parameters : IXrpcParameters<Parameters>, ICursorParameters<Parameters>
    {
        [XrpcParameter("actor")] public required AtIdentifier Actor { get; init; }
        /// <remarks>Lexicon: minimum 1, maximum 100, default 50.</remarks>
        [XrpcParameter("limit")] public int? Limit { get; init; }
        [XrpcParameter("cursor")] public string? Cursor { get; init; }
        [XrpcParameter("filter")] public FilterValue? Filter { get; init; }
        // generated IXrpcParameters<Parameters>.WriteTo / ReadFrom
    }

    /// <summary>Combinations of post/repost types to include in response.</summary>
    [JsonConverter(typeof(KnownValueConverter<FilterValue>))]
    public readonly partial record struct FilterValue(string Value) : IKnownValue<FilterValue>
    {
        public static FilterValue PostsWithReplies { get; } = new("posts_with_replies");
        public static FilterValue PostsNoReplies { get; } = new("posts_no_replies");
        public static FilterValue PostsWithMedia { get; } = new("posts_with_media");
        public bool IsKnown => Value is "posts_with_replies" or "posts_no_replies" or "posts_with_media" /* … */;
        public static FilterValue Create(string value) => new(value);
        public static implicit operator string(FilterValue value) => value.Value;
        public static implicit operator FilterValue(string value) => new(value);
    }

    public sealed partial record Output : ICursorPage<FeedViewPost>, ILexiconObject
    {
        [JsonPropertyName("cursor")] public string? Cursor { get; init; }
        [JsonPropertyName("feed")] public required IReadOnlyList<FeedViewPost> Feed { get; init; }
        IReadOnlyList<FeedViewPost> ICursorPage<FeedViewPost>.Items => Feed;
        [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
    }

    public static class Errors
    {
        public const string BlockedActor = "BlockedActor";
        public const string BlockedByActor = "BlockedByActor";
    }
}

// A procedure (ATProtoNet, com.atproto.repo.createRecord) and its namespace-client method.
public static partial class CreateRecord
{
    public static XrpcProcedure<Input, Output> Descriptor { get; } = /* … */;
    public sealed partial record Input : ILexiconObject
    {
        [JsonPropertyName("repo")] public required AtIdentifier Repo { get; init; }
        [JsonPropertyName("collection")] public required Nsid Collection { get; init; }
        [JsonPropertyName("rkey")] public RecordKey? Rkey { get; init; }
        [JsonPropertyName("validate")] public bool? Validate { get; init; }
        [JsonPropertyName("record")] public required DataModelValue Record { get; init; }
        [JsonPropertyName("swapCommit")] public Cid? SwapCommit { get; init; }
        [JsonExtensionData] public IDictionary<string, JsonElement>? ExtensionData { get; init; }
    }
    public sealed partial record Output : ILexiconObject { /* uri, cid, commit, validationStatus */ }
    public static class Errors { public const string InvalidSwap = "InvalidSwap"; }
}

public readonly partial struct RepoNamespace(AtProtoClient client)
{
    /// <summary>Create a single new repository record.</summary>
    public Task<CreateRecord.Output> CreateRecordAsync(CreateRecord.Input input, CancellationToken cancellationToken = default)
        => client.ProcedureAsync(CreateRecord.Descriptor, input, cancellationToken);
}

// Accessors (LEX-67): instance members for @root prefixes, extension properties elsewhere.
public partial class AtProtoClient { public RepoNamespace Repo => new(this); }
public static class BlueskyClientExtensions
{
    extension(AtProtoClient client) { public BskyNamespace Bsky => new(client); }
}
// Usage: await client.Bsky.Feed.GetAuthorFeedAsync(new() { Actor = alice, Filter = GetAuthorFeed.FilterValue.PostsNoReplies });
```

#### Models

- **LEX-38** (MUST) One generated type per def. Two defs with the same shape are still two types.
  *Source:* Decision · 0.8 merged shapes (`FeedResponse` serves 5 methods, `ConvoIdRequest` 10)
  that needed 86 `DefOf` drift mappings
- **LEX-39** (MUST) Object defs, records, `Input`/`Output`/`Message` objects become **sealed**
  `partial record` classes implementing `ILexiconObject`, with `init` properties; named object
  and record defs also implement `ILexiconType<TSelf>` (their `$type` string and contract), which
  union converters and `LexiconUnknown` use. Lexicon-required properties are `required`; optional
  properties are nullable, except optional unions (LEX-84). Generated types never use the C# 15
  `closed` modifier, which is reserved for hand-written SDK hierarchies.
  *Source:* [AG-7](../03-api-guidelines.md#types) · 0.8 CONTRIBUTING "Lexicon models and clients"
- **LEX-40** (MUST) Lexicon `nullable`: a required, nullable property is a nullable C# property
  that is always written (JSON `null` when null). A non-required, nullable property is an
  `Optional<T?>` that distinguishes absent (not written) from `null` (written as `null`). Upstream
  has three sites: `com.atproto.repo.putRecord#input.swapRecord` (`Optional<Cid?>`, which the
  records API exposes as `SwapRecordAbsent`, [XRC-50](xrpc-client.md)),
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
  | `string` (no format) | `string`, or `DecimalString` when `fieldTypes` maps the field (LEX-92) |
  | `string` format `did`, `handle`, `at-identifier`, `at-uri`, `nsid`, `cid`, `record-key`, `tid`, `datetime`, `language` | `Did`, `Handle`, `AtIdentifier`, `AtUri` (or `AtAddress`, LEX-88), `Nsid`, `Cid`, `RecordKey`, `Tid`, `AtDatetime`, `Language` |
  | `string` format `uri` | `string` (keeps the exact text; `System.Uri` normalises) |
  | other formats | the type the compilation or a referenced assembly declares with `[LexiconFormatType]` (`space-ref` → `SpaceUri`, declared in `ATProtoNet`), else `string` with a warning |
  | `string` with `knownValues` or `enum` | known-values struct (LEX-47) |
  | `bytes` | `ReadOnlyMemory<byte>` (`{"$bytes": …}`) |
  | `cid-link` | `CidLink` (`{"$link": …}`) |
  | `blob` | `BlobRef` |
  | `array` | `IReadOnlyList<T>` |
  | `unknown` | `DataModelValue` |
  | `ref` | the target's type |
  | `union` | C# 15 `union` struct (LEX-49); optional fields use the union itself (LEX-84) |

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
  `string` (they cannot fail, so [AG-8](../03-api-guidelines.md#types)'s ban on conversions from
  `string`, which is about identifiers, does not apply), and a generated converter. Any string round-trips. A `default`
  value (null `Value`) is not serializable. For `enum`, membership is enforced by
  `LexiconValidator`, not by deserialization.
  *Source:* [AG-9](../03-api-guidelines.md#types) · 0.8 `KnownValueConstants_MatchUpstream`;
  0.8 `RepoOpAction` was a closed C# enum for an open string (`SyncModels.cs:295`)
- **LEX-48** (MUST) Named string defs with `knownValues` (`com.atproto.moderation.defs#reasonType`)
  produce one struct referenced by every property that refs them.
  *Source:* Decision

#### Unions

- **LEX-49** (MUST) A Lexicon union is a generated C# 15 `union` type,
  `public union {Name}(CaseA, CaseB, …)`, named by LEX-34, carrying `[LexiconDefinition]` and
  `[JsonConverter(typeof({Name}Converter))]`. Its cases are the referenced defs' types in `refs`
  order, plus `LexiconUnknown` for open unions (LEX-50). A case type implements nothing
  union-specific, so any type can be a case of any number of unions in any assembly: first-party
  unions that cross Lexicon families (22 of 103 upstream) and users' unions that name SDK types
  need no extra mechanism. Union cases are reference types, so the union's single `object?`
  storage boxes nothing.
  ~~A union is a generated `partial interface` that each variant implements; a user assembly
  generates a derived record `{Union}{Variant}` for SDK types in its unions, which requires
  unsealed generated types.~~ Superseded by C# 15 unions
  ([D-18](../06-decisions.md#d-18-lexicon-unions-are-c-15-union-types), revised): interfaces could not span
  assemblies, and derived records cost the sealedness of every generated type.
  *Source:* [D-18](../06-decisions.md#d-18-lexicon-unions-are-c-15-union-types) ·
  [C# 15 union types](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union) ·
  Lexicon survey (cross-family union members: Ozone→core 10, Bluesky→core 6, Ozone→Bluesky 4,
  StandardSite→core 2)
- **LEX-50** (MUST) Open unions have the shared `LexiconUnknown` case (`Type` = the `$type` as
  received, `Raw` = a detached copy of the object), which writes back its raw JSON unchanged.
  Closed unions have no unknown case.
  *Source:* [D-18](../06-decisions.md#d-18-lexicon-unions-are-c-15-union-types) · 0.8
  `OpenUnionTests.Deserialize_UnknownVariant_ReadsAsUnknownAndWritesBackByteForByte`,
  `UnknownVariant_Constructor_DetachesRawFromTheCallersDocument`
- **LEX-51** (MUST) Union read (generated converter, not System.Text.Json's built-in union
  handling): buffer the object; find `$type` anywhere among its root properties (not only
  first); match escaped discriminators unescaped; dispatch to the matching case's source-generated
  `JsonTypeInfo`; for an unlisted `$type`, produce `LexiconUnknown` in an open union and throw
  `JsonException` with the JSON path in a closed one. A member that is not an object or has no
  `$type` throws `JsonException`; JSON `null` reads as the default union (no active case).
  *Source:* 0.8 #50 · `OutOfOrderTypeMetadataTests`, `OpenUnionTests.Deserialize_EscapedDiscriminatorValue_IsMatchedUnescaped`,
  `…UnknownVariantOfClosedUnion_Throws`, `…UnionMemberWithoutType_Throws`,
  `…UnionMemberThatIsNotAnObject_Throws`, `…NullUnionMember_ReadsAsNull` · .NET 11
  System.Text.Json serializes a C# union by writing only the active case, classifies by token type
  or property names, and offers no per-case contract (`JsonUnionCaseInfo` has only `CaseType` and
  `IsNullable`), so it cannot read or write atproto's `$type` format
  ([docs](https://learn.microsoft.com/dotnet/standard/serialization/system-text-json/union-types))
- **LEX-52** (MUST) One unknown variant never fails its container: a page with one unrecognised
  embed or thread node deserializes, with that member as `LexiconUnknown`.
  *Source:* 0.8 #115 · `Deserialize_UnknownEmbedInsidePostView_KeepsTheRestOfThePage`,
  `Deserialize_UnknownThreadNodeAmongReplies_KeepsTheKnownNodes`
- **LEX-53** (MUST) An invalid member inside a variant fails with the JSON path within the
  variant, and deeply nested unions fail after one deserialization attempt per level (no
  repeated re-reading). A variant converter cannot read past its value or stop short of it.
  *Source:* `Deserialize_InvalidMemberOfAVariant_NamesItsPathWithinTheVariant`,
  `…InvalidMemberUnderDeeplyNestedUnions_FailsFastWithTheErrorOfOneDeserialize`,
  `…VariantConverterThatReadsPastItsValue_CannotReachTheNextMember`
- **LEX-54** (MUST) Union write: the active case is written with `$type` as its **first**
  property (the NSID for a `main` def, `nsid#name` otherwise, from `ILexiconType<TSelf>`). A
  `LexiconUnknown` writes its raw JSON unchanged. **Outside union position**, a plain `ref` to an
  object writes **no** `$type`; record types always write `$type` (LEX-57). A default union (no
  active case) in an optional property is omitted (LEX-84). The mechanism is not normative; the
  reference approach serializes the case through its own contract into a pooled buffer and
  inserts `"$type":"…"` after the opening brace (records, which already lead with `$type`, are
  copied as they are), and generated per-type member writers are an allowed optimisation if
  benchmarks call for them. Putting a non-case type into a union is a compile error, so no
  runtime "undeclared variant" failure exists.
  *Source:* `OpenUnionTests.Serialize_KnownVariant_LeadsWithType…` ·
  [Lexicon spec: ref](https://atproto.com/specs/lexicon) ("When a `ref` points to an `object`
  type… the `$type` field should not be included in encoded data as a discriminator. This does
  not apply to references to `record` types, which always include the `$type` discriminator.")
- ~~**LEX-55** (MUST) `unionExtensions` lets a user def join an open union from a referenced
  assembly: the generated type implements that union's interface and `{Name}Lexicons.Register`
  adds it to a registry, so data with its `$type` reads as the typed variant.~~ Superseded by
  LEX-86: a C# 15 union's case list is fixed where it is declared, so a user type cannot join an
  SDK union; it travels as `LexiconUnknown` with typed helpers instead.
  *Source:* 0.8 `LexiconTypeRegistry.RegisterUnionVariant`
- **LEX-84** (MUST) A union-typed property that the Lexicon makes optional is typed as the union
  struct itself (not `Nullable<TUnion>`): `default` (no active case) means absent, is omitted on
  write, reads from an absent property or JSON `null`, and `post.Embed is null` tests for it (the
  C# `null` pattern on a union tests its value). A required union property is `required`; a
  missing or JSON-`null` required union fails deserialization with `JsonException`, and writing
  one left `default` throws `JsonException` naming the property (for example through a
  generated property-level converter).
  *Source:* Decision · [C# 15 union types: null and default](https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/union)
- **LEX-85** (MUST) The generated shape keeps the C# 15 union guarantees available to callers:
  each case type converts implicitly to the union (`Embed = new Images { … }`); type patterns
  test the active case (`post.Embed is Images images`); a `switch` over a closed union that
  handles every case needs no discard arm; an open union additionally needs a `LexiconUnknown`
  arm or a discard. A Lexicon update that adds a case therefore shows up as a compiler warning at
  every exhaustive `switch` that does not handle it. A compile test over generated code checks
  these (warnings as errors).
  *Source:* [D-18](../06-decisions.md#d-18-lexicon-unions-are-c-15-union-types) ·
  [What's new in C# 15](https://learn.microsoft.com/dotnet/csharp/whats-new/csharp-15)
- **LEX-86** (MUST) A user def that extends an open union declared in a referenced assembly (for
  example a custom embed in `app.bsky.feed.post#embed`) reads as `LexiconUnknown`, and
  `unknown.TryAs<MyEmbed>(out var embed)` deserializes it through the user type's contract when
  the `$type` equals `MyEmbed.LexiconType`. `LexiconUnknown.From(myEmbed)` builds the case for
  writing (`$type` first, then the user type's members). For each `unionExtensions` entry, the
  generator also emits C# 14 extension members on the SDK union (`embed.TryGetMyEmbed(out …)`,
  `PostEmbed.FromMyEmbed(…)`), so user code reads the same as for SDK cases.
  *Source:* 0.8 `LexiconTypeRegistry.RegisterUnionVariant` · `RegisteredVariant_OnSdkUnion_IsUsedByTheOptionsTheClientsUse`
  (scenario carried over in the new form)

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
  | subscription | `XrpcSubscription<Parameters, {Method}Message>` (the `message` union, LEX-34) |

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
  *Source:* Decision · 0.8 `XrpcErrors` had 28 names; upstream declares 286 method errors (124
  distinct names)
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
- **LEX-67** (MUST) Accessors: for `@root` prefixes (`com.atproto`, which sits in the same layer as
  the client core), the generator emits instance properties into a `partial class AtProtoClient`
  part (`client.Repo`). For every other accessor, first-party families in the same assembly
  included, it emits C# 14 extension properties
  (`extension(AtProtoClient client) { public BskyNamespace Bsky => new(client); }`), so the
  client core namespace never references the Lexicon families' namespaces (enforced by the
  architecture tests) and users' generated Lexicons attach the same way.
  *Source:* [D-13](../06-decisions.md#d-13-lexicon-namespace-access-through-extension-properties)
- **LEX-68** (MUST) Namespace clients expose no subscription methods. Subscriptions are consumed
  through the `ATProtoNet.Streaming` namespace with the generated `XrpcSubscription` descriptor
  ([streaming.md](streaming.md)) and served through [server.md](server.md). Message defs
  (`#commit`, `#identity`, …) are generated like object defs; the streaming layer binds the hot
  ones directly from DRISL-CBOR into these same types (D-10), so each message def still has
  exactly one type.
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
  shared pagination helper. A resulting name collision is a LEX-37 error.
  *Source:* [XRC-45](xrpc-client.md) · 0.8 kept 13 hand-written `Enumerate*`; generated, they cost
  nothing

#### Serialization contexts

- **LEX-72** (MUST) Each generation unit (LEX-30) has one source-generated `JsonSerializerContext`
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
- **LEX-76** (MUST) A `space` def becomes a static class exposing the declaration as
  `ATProtoNet.Spaces.SpaceTypeDeclaration` (in `ATProtoNet`, so always available to generated
  code), with no experimental marker. The `space:` scope builders of [auth.md](auth.md) accept it,
  so a space type's declared collections feed scope defaults ([SPC-63](spaces.md#oauth-scopes)).
  *Source:* 0.8 #92 · `Emit_SpaceDeclaration_*` · **Decision** (Spaces is core; maintainer review 2026-10-17)

#### Spaces families

- **LEX-87** (MUST) The generated `com.atproto.space.*` and `com.atproto.simplespace.*` families
  (`ATProtoNet.Lexicons.Com.AtProto.Space`, `…SimpleSpace`) are generated, tested and versioned like
  every other first-party family and carry no `[Experimental]` attribute. Their methods that need a
  space credential are reached through `SpaceClient` ([xrpc-client.md](xrpc-client.md#records-in-spaces)),
  which binds the audience; the generated descriptors carry no audience logic. Only if the GA
  specification is not out when 0.9.0 ships do these families take `ATPROTO001` through the
  `experimental` option, removed again in the 0.9.x release after GA ([spaces.md](spaces.md)
  header).
  *Source:* **Decision** (Spaces is core; maintainer review 2026-10-17) · [SPC-3](spaces.md#protocol-status-pins-and-placement)
- **LEX-88** (MUST) A Lexicon `at-uri` field maps to `AtAddress` ([syntax.md](syntax.md#space-addresses))
  instead of `AtUri` when it matches the `addressFields` option. The first-party configuration
  matches every `at-uri` field of the Spaces families and each field of another family that the
  pinned protocol revision says may address a space or a record in one; the list is reviewed at
  each Spaces pin. All other `at-uri` fields stay `AtUri`. The format `space-ref` maps to
  `SpaceUri` (LEX-43).
  *Source:* [SPC-52](spaces.md#addressing-implementation) · forum "Bringing back at://" (a single
  scheme lets `at-uri` fields carry space addresses)

### Hand-written code on top of generated code

- **LEX-77** (MUST) Hand-written helpers live outside `Generated/`, as members of the generated
  `partial` types (`BskyNamespace.Posting.cs`) or as extension members. They never redeclare a
  generated type, member, JSON name or wire shape.
  *Source:* [D-04](../06-decisions.md#d-04-generate-the-lexicon-surface-from-vendored-lexicon-json)
- **LEX-78** (MUST) Helpers that need a dynamic proxy (feed interactions to
  `{feedGenerator}#bsky_fg`) use `client.With(…)`, not descriptor defaults.
  *Source:* 0.8 `FeedClient.cs:419`

### Users' own Lexicons

- **LEX-79** (MUST) A user references `ATProtoNet` (which carries the generator, LEX-27) and lists
  schemas as `<AtProtoLexicon Include="lexicons/**/*.json" />`; `ATProtoNet`'s build props turn
  these into `AdditionalFiles` with metadata the generator reads. A project with no
  `AtProtoLexicon` items generates nothing and pays nothing beyond the analyzer load. An optional
  `lexgen.json` in the same items configures generation. Refs to first-party NSIDs resolve to the
  SDK's types (LEX-8), so users vendor only their own schemas.
  *Source:* Decision
- **LEX-80** (MUST) Generated user code has the same shape as first-party code: records work with
  `client.Records<T>()`, methods hang off `client.{Accessor}`, unions and known values behave
  identically. Generator diagnostics have IDs `ATLEX001`–`ATLEX099` and point at the Lexicon file
  and JSON pointer.
  *Source:* Decision

### Runtime Lexicon resolution

- **LEX-81** (MUST, experimental `ATPROTO004`) `LexiconResolver` (namespace
  `ATProtoNet.LexiconResolution`, in the network layer because it does I/O, which the foundation
  `ATProtoNet.Lexicon` namespace must not) resolves an NSID by querying the DNS TXT
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
| `MaxRefAliasDepth` | 8 | chains of named `ref` defs (LEX-7) |

The union converter buffers one object to find `$type` and searches only within it; the XRPC
success-body cap already bounds that object ([xrpc-client.md](xrpc-client.md)).

## Errors

| Failure | Result |
|---|---|
| Malformed Lexicon document | `FormatException` from `Parse`; diagnostics from `TryParse` |
| Lint findings, unresolved refs, collisions | `LexiconDiagnostic` (rule ID, severity, pointer); generator diagnostics `ATLEX0xx` |
| Data violates a schema | `LexiconValidationResult` with paths; `LexiconValidationException` where an API must throw |
| JSON does not fit a generated type (missing required, wrong type, invalid identifier, unknown variant of a closed union, missing or `null` required union) | `JsonException` with JSON path (wrapped by the XRPC layer as `XrpcResponseFormatException`) |
| Writing a required union property left `default` | `JsonException` naming the property |
| Putting a type that is not a case into a union | compile error (C# 15 union conversions) |
| `As<T>()` on a value of another `$type` | `InvalidOperationException` |
| Runtime resolution failure | `LexiconResolutionException : AtProtoException` |

## Conformance and tests

- **Vectors:** interop-tests `lexicon/` suites (newly vendored: schema validity and data
  validation), `lexicon-resolution.json`, the golden XRPC wire fixtures (run against generated
  clients).
- **Generator:** all vendored Lexicons generate and compile with zero diagnostics (extends #141,
  `CSharpEmitterCompileTests`); golden emitter output for a representative set (record, paginated
  query, procedure with binary input, open union, closed union, known values, nested defs,
  permission set, space def, and an object used both as a union case and as a plain `ref`, which
  writes `$type` only in union position); a compile test of generated unions with exhaustive
  switches under warnings-as-errors (LEX-85); CLI and source-generator output are byte-identical;
  CI regeneration check.
- **Scenarios to port:** `OpenUnionTests`, `OutOfOrderTypeMetadataTests`,
  `RecordTypeDiscriminatorTests`, `UnknownFieldTests`, `CustomUnionTests` (registry-based
  variants become `LexiconUnknown.TryAs`/`From` scenarios, LEX-86), `LexiconUnionTests`,
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
| Unions are C# 15 `union` types with generated converters and one shared `LexiconUnknown` case; no `Unknown*` hand classes, no duplicate embed types, no base classes or interfaces for variants | D-18 (revised), LEX-49 |
| Optional union fields are the union struct, `default` = absent (0.8: nullable base-class references) | LEX-84 |
| User types extending SDK open unions travel as `LexiconUnknown` with generated typed helpers instead of registry registration | LEX-86: C# 15 union cases are fixed at declaration |
| Generated code needs C# 15 and `net11.0` | D-25, LEX-83 |
| The source generator ships inside the `ATProtoNet` package; there is no generator package to add | One base package ([D-03](../06-decisions.md#d-03-package-map)) |
| A union variant in plain-`ref` position writes no `$type` (0.8 wrote it for union-variant types in every context) | Lexicon spec: `$type` should not be written for plain object refs; records always write it. Records the SDK writes that contain such objects in plain-ref positions therefore get different bytes, and different CIDs, than 0.8 produced, intentionally |
| No `AtProtoRecord` base class or #49 contract modifier; `createdAt` stamping via `ICreatedAtRecord<TSelf>` | One record pattern |
| `integer` maps by the D-19 rule (0.8: `int` 189×, `long` 33×, no rule) | D-19 |
| Known values are open structs; closed C# enums for open strings (`RepoOpAction`) are gone | AG-9 |
| Methods take one parameters or input record; per-call options go through `client.With(…)` | D-12 |
| Case-sensitive reads, no numbers from strings | D-20 |
| `fieldTypes` maps a plain `string` field that carries a number to `DecimalString` | LEX-92, D-20 |
| `language` maps to `Language` (0.8: `string`) | Typed identifiers everywhere ([syntax.md](syntax.md)) |
| Non-required `nullable` fields are `Optional<T?>`; 0.8 could not send `swapRecord: null` | LEX-40 |
| `LexiconTypeRegistry` is an instance holding records only; `ILexiconPlugin` is replaced by generated `Register` | D-10 |
| Diff treats loosening a constraint and making a field optional as breaking (0.8: compatible) | Lexicon versioning guide |
| Unresolvable refs are errors (0.8: `JsonElement?` with a warning) | LEX-6 |
| Generated `ToString()` prints no members | AG-29 |
| `com.atproto.space.*`/`simplespace.*` are generated first-party families without `ATPROTO001`; `LexSpace` and `space` defs are not experimental | LEX-87; Spaces is a core data protocol targeting GA |
| `at-uri` fields that may address spaces map to `AtAddress` | LEX-88 |

## Open points

Defaults accepted (maintainer, 2026-10-17). The implementing phase applies them without further
sign-off, settles any point left to it, follows upstream where a point waits on upstream, and
records the outcome here in the same PR.

| # | Question | Default |
|---|---|---|
| LEX-O1 | Align `lexicons.json` exactly with `@atproto/lex`'s manifest (`version`, `lexicons`, `resolvers`, `resolutions`) so one manifest serves several SDKs? | Yes where the formats overlap; our extra fields (CID-in-record-form, provenance) go under an `x-atprotonet` key. Verify against `@atproto/lex-installer` at phase 3 start. |
| LEX-O2 | Closed `enum` as an open struct (LEX-47) or a C# `enum`? | Open struct, lenient read, validator-enforced; a C# enum breaks readers when upstream breaks the rule. |
| LEX-O3 | Exact extension-data property type (`IDictionary<string, JsonElement>` vs `JsonObject`) under STJ source generation with `init`. | Whichever STJ source generation supports with `init` and no reflection; decided in the phase 3 spike. |
| ~~LEX-O4~~ | ~~Union membership of SDK types in users' unions: derived records or wrapper adapters?~~ | **Resolved** by C# 15 unions (D-18 revised): any type can be a case of any union, so neither is needed. |
| LEX-O5 | Generate `Enumerate*Async` for every paginated query (LEX-71) or only listed ones? | Every one. |
| LEX-O6 | Direct DRISL-CBOR binding for generated types (skipping `DataModelValue`)? | Later optimisation behind the same API (D-10). |
| LEX-O7 | Should `app.bsky.unspecced.*` be generated at all? | Yes, marked `ATPROTO002`. |
| LEX-O8 | C# 15 union details (syntax, null semantics, runtime types) may still change before .NET 11 GA (November 2026). | Re-check against the GA compiler at phase 3 start. The emitter isolates the union shape, so a change is a regeneration, not a redesign. |
| LEX-O9 | System.Text.Json's built-in union support cannot express `$type` discriminators today (LEX-51). | Keep the generated converters; revisit if a later System.Text.Json adds discriminator-based union classification and per-case contracts. |
| LEX-O10 | How does the generator know which `at-uri` fields may hold space addresses (LEX-88)? | The `addressFields` option, reviewed at each Spaces pin; switch to a Lexicon-level marker if the GA specification defines one. |
