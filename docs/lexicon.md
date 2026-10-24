# Lexicon

`ATProtoNet.Lexicon` reads Lexicon schemas into one typed model, checks them (lint and
compatibility rules), and validates data against them. `ATProtoNet.Lexicon.Runtime` is what
generated code builds on, and the `atproto-lexgen` tool turns Lexicons into that code. See the
[Lexicon specification](https://atproto.com/specs/lexicon).

## Schemas

`LexiconDocument.Parse` reads a schema file and refuses one that breaks the Lexicon rules (a
primary type that is not `main`, a named `ref`, a parameter that is an object, …); `TryParse`
returns every finding instead, each with a rule ID (`LXL001` onward) and a JSON pointer. A `type`
this version does not know is kept as `LexUnknownType` with a warning, so newer schemas stay
readable. The document keeps the bytes it was parsed from, and `ComputeRecordCid` gives the CID
its `com.atproto.lexicon.schema` record has when published unchanged.

A `LexiconCatalog` is a closed set of documents. It resolves `ref` and `union` targets in all
three forms (`nsid`, `nsid#name`, `#name`) and reports the ones that resolve to nothing, point at a
token, or nest a union in a union. `LexiconLint.Run` adds the permission-set, space and style rules.

```csharp
using ATProtoNet;
using ATProtoNet.Lexicon;

var document = LexiconDocument.Parse(File.ReadAllBytes("lexicons/com/example/post.json"));
var catalog = LexiconCatalog.Create([document]);
foreach (var finding in catalog.Diagnostics.Concat(LexiconLint.Run(catalog)))
    Console.WriteLine(finding);     // com.example.post /defs/main: warning LXL041: …
```

`LexiconCompatibility.Compare(baseline, current)` classifies every change between two catalogs
under the Lexicon versioning rule: a published schema may only add optional constraints to fields
that had none. Removing or loosening anything, making a field required or optional, or adding a
member to a closed union is breaking; the report suggests a `revision` for every schema whose
changes are all compatible.

## Validating data

`LexiconValidator` checks a `DataModelValue` against a definition and reports every violation with
its data path. It keeps Lexicon's open semantics: unknown properties pass, `knownValues` are not
enforced, and an open union accepts any object with a `$type`. `ValidateRecord` also checks the
record's `$type` and its key against the record's key type.

```csharp continued
using ATProtoNet.Data;
using ATProtoNet.Lexicon.Validation;

var record = DataModelValue.FromJson("""{"$type":"com.example.post","text":"hi"}"""u8);
var result = new LexiconValidator(catalog).ValidateRecord(document.Id, RecordKey.Parse("3jzfcijpj2z2a"), record);
foreach (var violation in result.Violations)
    Console.WriteLine($"{violation.Path}: {violation.Message}");    // $.text: …
```

Clients validate only when asked: the server is the authority, and a client with an older schema
must not reject newer data.

## Generated code

Generated code needs C# 15 and .NET 11. Namespaces follow the NSID (`app.bsky.feed.post` →
`ATProtoNet.Lexicons.App.Bsky.Feed`), type names read like hand-written ones (`Post`, `PostView`,
`GetTimeline`). Each definition is one type:

- **Objects and records** are sealed records with `init` properties and an exact
  `[JsonPropertyName]` per field. Properties the schema does not declare round-trip through
  `ExtensionData`. A record writes its `$type` first; `ToString` prints no member values.
- **Unions** are C# 15 `union` types. An open union has the extra case `LexiconUnknown`, which
  writes the object back unchanged; a closed one rejects other `$type`s. An optional union property
  left `default` is absent.
- **Known values** are structs over the string: any string reads and writes, `IsKnown` tells the
  listed ones apart.
- **Methods** are static classes with a `Descriptor`, nested `Parameters`, `Input`, `Output` and
  `Errors`; paginated queries also get an `Enumerate…Async` on their namespace client.

```csharp partial
var post = new Post { Text = "hi", CreatedAt = AtDatetime.Now(), Embed = new Images { Images = [image] } };
string kind = post.Embed switch
{
    Images => "images",
    External => "link",
    LexiconUnknown unknown => unknown.Type,    // a kind this version does not know
    null => "none",
    _ => "other",
};
```

`DataModelValue.As<T>()` reads an `unknown` field, such as `PostView.Record`, as a generated
record; `AsKnownRecord(registry)` picks the type by `$type` from a `LexiconTypeRegistry` that the
generated `{Name}Lexicons.Register` methods fill.

## `atproto-lexgen`

The `atproto-lexgen` tool vendors and checks Lexicons and generates C#. Every rule it applies comes
from the library; exit codes are 0 (success), 1 (findings), 2 (usage), 3 (input) and 4 (network).

```text
atproto-lexgen install app.bsky.* --source atproto       # copy schemas into lexicons/ and lexicons.json
atproto-lexgen update --commit <sha> --dry-run            # what moving the pin would change
atproto-lexgen lint --strict                              # the lint rules, warnings fail too
atproto-lexgen generate --out Generated                   # C# from lexicons.json and lexgen.json
atproto-lexgen generate --check                           # exit 1 if the checked-in code is stale
```

`lexicons.json` records where each schema came from (a git commit, a directory or, from 0.9's
network support, a publishing account) and its record CID; `generate` refuses a vendored file that
no longer matches its CID. `lexgen.json` holds the few generation options: the namespace root,
generation units, accessor names, `renames` for names that collide or read badly, `fieldTypes` for
numbers carried in strings, default proxies and experimental markers.
