using System.Collections.Immutable;
using System.Text.Json;
using ATProtoNet.LexiconGenerator.CodeGen;
using ATProtoNet.LexiconGenerator.Schema;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// Compiles the C# the emitter produces, instead of only comparing it as text. Issue #141:
/// <see cref="CSharpEmitterTests"/> asserts on emitted strings, which is why a space
/// definition's generated <c>Collections</c> forwarder (<c>IReadOnlyList&lt;string&gt;</c>
/// assigned from a <c>SpaceTypeDeclaration.Collections</c> of <c>IReadOnlyList&lt;Nsid&gt;</c>,
/// CS0266) shipped without anything catching it.
/// </summary>
public class CSharpEmitterCompileTests
{
    private static readonly JsonSerializerOptions s_options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    private static LexiconDocument Parse(string json)
        => JsonSerializer.Deserialize<LexiconDocument>(json, s_options)!;

    // ── Fixtures not already covered by CSharpEmitterTests ──────────────────

    /// <summary>
    /// A record exercising every primitive type/format, an inline union (open) and a
    /// closed union, a <c>knownValues</c> string, a ref to a string-enum def, and refs to
    /// definitions in another document — both same-namespace (sibling NSID authority) and a
    /// different one, to hit both branches of <c>CSharpEmitter.Shorten</c>.
    /// </summary>
    private const string ItemRecord = """
        {
          "lexicon": 1,
          "id": "com.example.compile.item",
          "defs": {
            "main": {
              "type": "record",
              "key": "tid",
              "record": {
                "type": "object",
                "required": ["name", "createdAt"],
                "properties": {
                  "name": { "type": "string" },
                  "createdAt": { "type": "string", "format": "datetime" },
                  "did": { "type": "string", "format": "did" },
                  "handle": { "type": "string", "format": "handle" },
                  "actor": { "type": "string", "format": "at-identifier" },
                  "subject": { "type": "string", "format": "at-uri" },
                  "collection": { "type": "string", "format": "nsid" },
                  "cid": { "type": "string", "format": "cid" },
                  "rkey": { "type": "string", "format": "record-key" },
                  "rev": { "type": "string", "format": "tid" },
                  "savedAt": { "type": "string", "format": "datetime" },
                  "homepage": { "type": "string", "format": "uri" },
                  "lang": { "type": "string", "format": "language" },
                  "category": {
                    "type": "string",
                    "knownValues": ["com.example.compile.item#foo", "com.example.compile.item#bar"]
                  },
                  "count": { "type": "integer", "minimum": 0, "maximum": 100 },
                  "weight": { "type": "number" },
                  "active": { "type": "boolean", "default": true },
                  "misc": { "type": "unknown" },
                  "link": { "type": "cid-link" },
                  "avatar": { "type": "blob", "accept": ["image/*"], "maxSize": 1000000 },
                  "raw": { "type": "bytes" },
                  "tags": { "type": "array", "items": { "type": "string" } },
                  "counts": { "type": "array", "items": { "type": "integer" } },
                  "mentions": { "type": "array", "items": { "type": "string", "format": "did" } },
                  "status": { "type": "ref", "ref": "#status" },
                  "widget": { "type": "ref", "ref": "com.example.compile.shared#widget" },
                  "gadget": { "type": "ref", "ref": "com.other.authority.shared#gadget" },
                  "attribution": { "type": "union", "refs": ["#personVariant", "#orgVariant"] },
                  "access": {
                    "type": "union",
                    "closed": true,
                    "refs": ["#readVariant", "#writeVariant"]
                  }
                }
              }
            },
            "status": { "type": "string", "enum": ["active", "inactive", "pending"] },
            "personVariant": { "type": "object", "properties": { "name": { "type": "string" } } },
            "orgVariant": {
              "type": "object",
              "properties": { "name": { "type": "string" }, "taxId": { "type": "string" } }
            },
            "readVariant": { "type": "object", "properties": { "scope": { "type": "string" } } },
            "writeVariant": {
              "type": "object",
              "properties": { "scope": { "type": "string" }, "mode": { "type": "string" } }
            }
          }
        }
        """;

    /// <summary>Sibling-authority document the record above refs into the same C# namespace.</summary>
    private const string SharedWidget = """
        {
          "lexicon": 1,
          "id": "com.example.compile.shared",
          "defs": {
            "widget": { "type": "object", "properties": { "label": { "type": "string" } } }
          }
        }
        """;

    /// <summary>Different-authority document, so the ref into it must be <c>global::</c>-qualified.</summary>
    private const string OtherAuthorityGadget = """
        {
          "lexicon": 1,
          "id": "com.other.authority.shared",
          "defs": {
            "gadget": { "type": "object", "properties": { "serial": { "type": "string" } } }
          }
        }
        """;

    /// <summary>A space declaration with no collections and no localized names (all defaulted).</summary>
    private const string BareSpace = """
        {
          "lexicon": 1,
          "id": "com.example.compile.bookmarks",
          "defs": {
            "main": { "type": "space", "key": "any", "name": "Bookmarks", "collections": [] }
          }
        }
        """;

    /// <summary>
    /// Queries and procedures are skipped by the emitter (Issue #45 scope), so this only has to
    /// prove the surrounding object defs still compile when a query/procedure sits next to them.
    /// </summary>
    private const string QueryWithParamsInputOutput = """
        {
          "lexicon": 1,
          "id": "com.example.compile.getThing",
          "defs": {
            "main": {
              "type": "query",
              "parameters": {
                "type": "params",
                "properties": { "limit": { "type": "integer" }, "cursor": { "type": "string" } }
              },
              "input": { "encoding": "application/json", "schema": { "type": "ref", "ref": "#result" } },
              "output": { "encoding": "application/json", "schema": { "type": "ref", "ref": "#result" } }
            },
            "result": {
              "type": "object",
              "properties": { "items": { "type": "array", "items": { "type": "string" } } }
            }
          }
        }
        """;

    private const string ProcedureWithParamsInputOutput = """
        {
          "lexicon": 1,
          "id": "com.example.compile.doThing",
          "defs": {
            "main": {
              "type": "procedure",
              "input": {
                "encoding": "application/json",
                "schema": { "type": "object", "properties": { "value": { "type": "string" } } }
              },
              "output": { "encoding": "application/json", "schema": { "type": "ref", "ref": "#doThingOutput" } }
            },
            "doThingOutput": { "type": "object", "properties": { "ok": { "type": "boolean" } } }
          }
        }
        """;

    /// <summary>A subscription with a closed union of frame types — also currently skipped.</summary>
    private const string SubscriptionWithFrames = """
        {
          "lexicon": 1,
          "id": "com.example.compile.subscribeThing",
          "defs": {
            "main": {
              "type": "subscription",
              "parameters": { "type": "params", "properties": { "cursor": { "type": "integer" } } },
              "message": { "schema": { "type": "union", "closed": true, "refs": ["#frameA", "#frameB"] } }
            },
            "frameA": { "type": "object", "properties": { "seq": { "type": "integer" } } },
            "frameB": { "type": "object", "properties": { "reason": { "type": "string" } } }
          }
        }
        """;

    [Fact]
    public void Emit_RepresentativeLexiconSet_ProducesCompilableCSharp()
    {
        var documents = new[]
        {
            // Reused from CSharpEmitterTests: records, tokens/token families, arrays, blobs,
            // inline nested objects, an open union of object defs, a union of tokens, a ref
            // across two documents (sibling NSID authority), and an SDK-model ref.
            CSharpEmitterTests.RecipeDefs,
            CSharpEmitterTests.RecipeRecord,
            // A space def with collections and localized names.
            CSharpEmitterTests.ForumSpace,
            // A permission set.
            CSharpEmitterTests.AuthBasicSet,
            // New fixtures covering what those don't: every primitive/format, knownValues, a
            // string-enum ref, a closed union, cross-namespace refs, a space without
            // collections/localized names, and query/procedure/subscription defs.
            ItemRecord,
            SharedWidget,
            OtherAuthorityGadget,
            BareSpace,
            QueryWithParamsInputOutput,
            ProcedureWithParamsInputOutput,
            SubscriptionWithFrames,
        };

        var emitter = new CSharpEmitter("Compile.Test.Lexicon");
        var files = emitter.EmitAll(documents.Select(Parse));

        Assert.NotEmpty(files);

        var diagnostics = CompileAll(files);
        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

        Assert.True(errors.Count == 0,
            "Generated C# failed to compile:\n" + string.Join("\n", errors.Select(e => e.ToString())));
    }

    /// <summary>
    /// Compiles a set of generated files in memory with Roslyn, referencing the trusted
    /// platform assemblies plus the SDK assembly the generated code targets.
    /// </summary>
    private static ImmutableArray<Diagnostic> CompileAll(List<(string Path, string Content)> files)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var trees = files
            .Select(f => CSharpSyntaxTree.ParseText(f.Content, parseOptions, path: f.Path))
            .ToList();

        var trustedPlatformAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator);

        var references = trustedPlatformAssemblies
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .Append(MetadataReference.CreateFromFile(typeof(AtProtoClient).Assembly.Location))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "LexgenCompileTest",
            trees,
            references,
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));

        return compilation.GetDiagnostics();
    }
}
