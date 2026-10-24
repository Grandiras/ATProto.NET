using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26).
namespace ATProtoNet.Lexicon;

/// <summary>A definition a reference points at: an NSID and a definition name.</summary>
/// <param name="Nsid">The document's NSID.</param>
/// <param name="DefName">The definition's name; <c>main</c> when the reference names none.</param>
readonly partial record struct LexRefTarget(Nsid Nsid, string DefName)
{
    /// <summary>Parses a reference as written in a Lexicon: <c>nsid</c>, <c>nsid#name</c>, or <c>#name</c> within <paramref name="context"/>.</summary>
    /// <param name="reference">The reference.</param>
    /// <param name="context">The NSID of the document the reference appears in.</param>
    /// <param name="target">The target.</param>
    /// <returns>Whether <paramref name="reference"/> is well formed.</returns>
    public static bool TryParse(string reference, Nsid context, out LexRefTarget target)
    {
        target = default;
        var hash = reference.IndexOf('#');
        var name = hash < 0 ? "main" : reference.Substring(hash + 1);
        if (name.Length == 0) return false;
        if (hash == 0)
        {
            target = new(context, name);
            return true;
        }
        if (!Nsid.TryParse(hash < 0 ? reference : reference.Substring(0, hash), out var nsid)) return false;
        target = new(nsid, name);
        return true;
    }

    /// <summary>The reference in its canonical form: <c>nsid</c> for <c>main</c>, <c>nsid#name</c> otherwise.</summary>
    /// <returns>The reference.</returns>
    public override string ToString() => DefName == "main" ? Nsid.ToString() : Nsid + "#" + DefName;
}

/// <summary>What kind of definition an external reference resolves to.</summary>
#if LEXICON_CODEGEN
internal
#else
public
#endif
enum ExternalDefinitionKind
{
    /// <summary>An object (or a record's object, under its record type).</summary>
    Object,

    /// <summary>A record.</summary>
    Record,

    /// <summary>A token: a constant string.</summary>
    Token,

    /// <summary>A string with known values or an enum.</summary>
    KnownValues,

    /// <summary>A named union.</summary>
    Union,

    /// <summary>Another kind; references to it cannot be generated.</summary>
    Other,
}

/// <summary>A definition provided outside the catalog, such as a generated type in a referenced assembly (LEX-8).</summary>
/// <param name="Target">The definition.</param>
/// <param name="TypeName">The fully qualified C# type name, without <c>global::</c>.</param>
/// <param name="Kind">What kind of definition it is.</param>
sealed partial record ExternalDefinition(LexRefTarget Target, string TypeName, ExternalDefinitionKind Kind);

/// <summary>Definitions outside a catalog's documents.</summary>
partial interface IExternalLexicons
{
    /// <summary>Finds a definition.</summary>
    /// <param name="target">The definition.</param>
    /// <param name="definition">The definition, when found.</param>
    /// <returns>Whether the definition exists.</returns>
    bool TryResolve(LexRefTarget target, [NotNullWhen(true)] out ExternalDefinition? definition);
}

/// <summary>A resolved definition: in one of the catalog's documents, or external.</summary>
/// <param name="Target">The definition.</param>
/// <param name="Document">The document, for a definition in the catalog.</param>
/// <param name="Definition">The schema, for a definition in the catalog.</param>
/// <param name="External">The external definition, otherwise.</param>
sealed partial record ResolvedDefinition(LexRefTarget Target, LexiconDocument? Document, LexUserType? Definition, ExternalDefinition? External);

/// <summary>A closed set of Lexicon documents, plus definitions outside it, with every reference resolved.</summary>
sealed partial class LexiconCatalog
{
    private readonly Dictionary<string, LexiconDocument> _documents;
    private readonly IExternalLexicons? _external;
    private readonly List<LexiconDiagnostic> _diagnostics = [];

    private LexiconCatalog(Dictionary<string, LexiconDocument> documents, IExternalLexicons? external)
    {
        _documents = documents;
        _external = external;
    }

    /// <summary>The documents, sorted by NSID.</summary>
    public IReadOnlyList<LexiconDocument> Documents => [.. _documents.Values.OrderBy(d => d.Id.ToString(), StringComparer.Ordinal)];

    /// <summary>Unresolved references, references to the wrong kind of definition, and structures that cannot terminate.</summary>
    public IReadOnlyList<LexiconDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Builds a catalog and checks every reference in it.</summary>
    /// <param name="documents">The documents; NSIDs are unique.</param>
    /// <param name="external">Definitions outside the documents, if any.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="ArgumentException">Two documents have the same NSID.</exception>
    public static LexiconCatalog Create(IEnumerable<LexiconDocument> documents, IExternalLexicons? external = null)
    {
        var map = new Dictionary<string, LexiconDocument>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (map.ContainsKey(document.Id.ToString())) throw new ArgumentException($"Two documents have the NSID {document.Id}.", nameof(documents));
            map[document.Id.ToString()] = document;
        }
        var catalog = new LexiconCatalog(map, external);
        catalog.Check();
        return catalog;
    }

    /// <summary>Finds a document by NSID.</summary>
    /// <param name="nsid">The NSID.</param>
    /// <param name="document">The document.</param>
    /// <returns>Whether the catalog has it.</returns>
    public bool TryGetDocument(Nsid nsid, [NotNullWhen(true)] out LexiconDocument? document) => _documents.TryGetValue(nsid.ToString(), out document);

    /// <summary>Resolves a reference.</summary>
    /// <param name="target">The target.</param>
    /// <param name="definition">The definition.</param>
    /// <returns>Whether it resolves.</returns>
    public bool TryResolve(LexRefTarget target, [NotNullWhen(true)] out ResolvedDefinition? definition)
    {
        definition = _documents.TryGetValue(target.Nsid.ToString(), out var document)
            ? document.Defs.TryGetValue(target.DefName, out var def) ? new(target, document, def, null) : null
            : _external is not null && _external.TryResolve(target, out var external) ? new(target, null, null, external) : null;
        return definition is not null;
    }

    /// <summary>Resolves a reference as written in <paramref name="context"/>.</summary>
    /// <param name="reference">The reference: <c>nsid</c>, <c>nsid#name</c> or <c>#name</c>.</param>
    /// <param name="context">The NSID of the document it appears in.</param>
    /// <param name="definition">The definition.</param>
    /// <returns>Whether it resolves.</returns>
    public bool TryResolve(string reference, Nsid context, [NotNullWhen(true)] out ResolvedDefinition? definition)
    {
        definition = null;
        return LexRefTarget.TryParse(reference, context, out var target) && TryResolve(target, out definition);
    }

    // LEX-6, LEX-7, LEX-9.
    private void Check()
    {
        foreach (var document in Documents)
            foreach (var def in document.Defs)
            {
                var pointer = "/defs/" + LexiconParser.Escape(def.Key);
                Walk(document, def.Value, pointer);
            }
        CheckRecursion();
    }

    private void Error(string rule, string message, LexiconDocument document, string pointer) =>
        _diagnostics.Add(new(rule, LexiconSeverity.Error, message, document.Id.ToString(), pointer));

    private void Walk(LexiconDocument document, LexSchema schema, string pointer)
    {
        switch (schema)
        {
            case LexRef reference:
                if (!TryResolve(reference.Ref, document.Id, out var resolved))
                    Error(LexiconRules.UnresolvedRef, $"\"{reference.Ref}\" resolves to nothing.", document, pointer + "/ref");
                else if (resolved.Definition is LexToken || resolved.External?.Kind == ExternalDefinitionKind.Token)
                    Error(LexiconRules.InvalidRef, $"\"{reference.Ref}\" is a token; a ref cannot point at one.", document, pointer + "/ref");
                break;
            case LexUnion union:
                for (var i = 0; i < union.Refs.Count; i++)
                    if (!TryResolve(union.Refs[i], document.Id, out var member))
                        Error(LexiconRules.UnresolvedRef, $"\"{union.Refs[i]}\" resolves to nothing.", document, pointer + "/refs/" + i);
                    else if (!(member.Definition is LexObject or LexRecord || member.External?.Kind is ExternalDefinitionKind.Object or ExternalDefinitionKind.Record or ExternalDefinitionKind.Other))
                        Error(LexiconRules.InvalidRef, $"\"{union.Refs[i]}\" is not an object or record; a union member must be one.", document, pointer + "/refs/" + i);
                break;
            case LexRecord record:
                Walk(document, record.Record, pointer + "/record");
                break;
            case LexObject obj:
                foreach (var property in obj.Properties) Walk(document, property.Value, pointer + "/properties/" + LexiconParser.Escape(property.Key));
                break;
            case LexParams parameters:
                foreach (var property in parameters.Properties) Walk(document, property.Value, pointer + "/properties/" + LexiconParser.Escape(property.Key));
                break;
            case LexArray array:
                Walk(document, array.Items, pointer + "/items");
                break;
            case LexQuery query:
                WalkMethod(document, query.Parameters, null, query.Output, pointer);
                break;
            case LexProcedure procedure:
                WalkMethod(document, procedure.Parameters, procedure.Input, procedure.Output, pointer);
                break;
            case LexSubscription subscription:
                WalkMethod(document, subscription.Parameters, null, subscription.Message, pointer, "message");
                break;
        }
    }

    private void WalkMethod(LexiconDocument document, LexParams? parameters, LexBody? input, LexBody? output, string pointer, string outputName = "output")
    {
        if (parameters is not null) Walk(document, parameters, pointer + "/parameters");
        if (input?.Schema is { } inputSchema) Walk(document, inputSchema, pointer + "/input/schema");
        if (output?.Schema is { } outputSchema) Walk(document, outputSchema, pointer + "/" + outputName + "/schema");
    }

    // LEX-9: an object that needs itself through required, non-nullable, non-array, non-union refs can never be written.
    private void CheckRecursion()
    {
        var state = new Dictionary<LexRefTarget, bool>(); // false: on the stack; true: done
        foreach (var document in Documents)
            foreach (var def in document.Defs)
                if (def.Value is LexObject or LexRecord) Visit(new(document.Id, def.Key), state, document, "/defs/" + LexiconParser.Escape(def.Key));
    }

    private void Visit(LexRefTarget target, Dictionary<LexRefTarget, bool> state, LexiconDocument origin, string pointer)
    {
        if (state.TryGetValue(target, out var done))
        {
            if (!done) Error(LexiconRules.InfiniteRecursion, $"{target} requires itself through required fields, so no value of it can exist.", origin, pointer);
            return;
        }
        if (!TryResolve(target, out var resolved) || resolved.Definition is not (LexObject or LexRecord)) return;
        state[target] = false;
        var obj = resolved.Definition as LexObject ?? ((LexRecord)resolved.Definition).Record;
        foreach (var name in obj.Required)
            if (!obj.Nullable.Contains(name) && obj.Properties.TryGetValue(name, out var property) && property is LexRef reference
                && LexRefTarget.TryParse(reference.Ref, target.Nsid, out var next))
                Visit(next, state, resolved.Document!, "/defs/" + LexiconParser.Escape(target.DefName) + "/properties/" + LexiconParser.Escape(name));
        state[target] = true;
    }
}
