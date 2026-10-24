using System;
using System.Collections.Generic;
using System.Linq;

namespace ATProtoNet.Lexicon.CodeGen;

// A C# type for a schema: its name, and whether it is a value type or a union (which change how properties are written).
internal readonly record struct CsType(string Name, bool IsValueType = false, bool IsUnion = false, CsType[]? Element = null, bool IsKnownValues = false)
{
    public static CsType Of(string name, bool isValueType = false) => new(name, isValueType);

    public bool IsList => Element is not null;

    public CsType ItemType => Element![0];
}

// Pass two support: the C# type of every schema (LEX-43, LEX-88, LEX-92).
internal sealed partial class Planner
{
    private static readonly Dictionary<string, string> Formats = new(StringComparer.Ordinal)
    {
        ["did"] = "global::ATProtoNet.Did",
        ["handle"] = "global::ATProtoNet.Handle",
        ["at-identifier"] = "global::ATProtoNet.AtIdentifier",
        ["at-uri"] = "global::ATProtoNet.AtUri",
        ["nsid"] = "global::ATProtoNet.Nsid",
        ["cid"] = "global::ATProtoNet.Cid",
        ["record-key"] = "global::ATProtoNet.RecordKey",
        ["datetime"] = "global::ATProtoNet.AtDatetime",
        ["language"] = "global::ATProtoNet.Language",
        ["tid"] = "global::ATProtoNet.Tid",
        ["uri"] = "string",
    };

    private readonly HashSet<string> _warnedFormats = new(StringComparer.Ordinal);

    // The type of a schema met at `path` ("nsid#def.prop") in the document `context`.
    public CsType TypeOf(LexSchema schema, Nsid context, string path)
    {
        switch (schema)
        {
            case LexBoolean:
                return CsType.Of("bool", true);
            case LexInteger integer:
                return CsType.Of(integer is { Minimum: >= int.MinValue, Maximum: <= int.MaxValue } ? "int" : "long", true);
            case LexString text:
                return StringType(text, context, path);
            case LexBytes:
                return CsType.Of("global::System.ReadOnlyMemory<byte>", true);
            case LexCidLink:
                return CsType.Of("global::ATProtoNet.Data.CidLink");
            case LexBlob:
                return CsType.Of("global::ATProtoNet.Data.BlobRef");
            case LexArray array:
                var item = TypeOf(array.Items, context, path);
                return new($"global::System.Collections.Generic.IReadOnlyList<{item.Name}>", Element: [item]);
            case LexObject or LexUnion when Inline(schema) is { } decl:
                return Of(decl);
            case LexRef reference:
                return RefType(reference.Ref, context, path);
            default:
                return CsType.Of("global::ATProtoNet.Data.DataModelValue");
        }
    }

    public static CsType Of(TypeDecl decl) => new(decl.Ref, decl.Kind is DeclKind.Union or DeclKind.KnownValues, decl.Kind == DeclKind.Union, IsKnownValues: decl.Kind == DeclKind.KnownValues);

    private CsType StringType(LexString text, Nsid context, string path)
    {
        if (Inline(text) is { } inline) return Of(inline);
        if (_config.FieldTypes.TryGetValue(path, out var fieldType))
        {
            UsedFieldTypes.Add(path);
            if (fieldType == "decimal" && text is { Format: null, KnownValues: null, Enum: null }) return CsType.Of("global::ATProtoNet.Data.DecimalString");
            Diagnostics.Add(new("ATLEX004", GenerationSeverity.Error,
                fieldType == "decimal" ? $"{path} is not a plain string (it has a format, knownValues or enum), so it cannot be a decimal." : $"\"fieldTypes\" maps fields only to \"decimal\", not \"{fieldType}\".",
                context.ToString(), path));
        }
        if (text.Format is null) return CsType.Of("string");
        if (text.Format == "at-uri" && Naming.MatchesAny(_config.AddressFields, path)) return CsType.Of("global::ATProtoNet.AtAddress", true);
        if (Formats.TryGetValue(text.Format, out var type) || _external?.FormatTypes.TryGetValue(text.Format, out type) == true) return CsType.Of(type!.StartsWith("global::", StringComparison.Ordinal) || type == "string" ? type : "global::" + type, text.Format == "tid");
        if (_warnedFormats.Add(text.Format))
            Diagnostics.Add(new("ATLEX005", GenerationSeverity.Warning, $"The string format \"{text.Format}\" has no type; such fields are strings.", context.ToString(), path));
        return CsType.Of("string");
    }

    private CsType RefType(string reference, Nsid context, string path)
    {
        if (!LexRefTarget.TryParse(reference, context, out var target) || !_catalog.TryResolve(target, out var resolved))
            return CsType.Of("global::ATProtoNet.Data.DataModelValue"); // reported by the catalog
        if (resolved.External is { } external)
            return new("global::" + external.TypeName, external.Kind is ExternalDefinitionKind.Union or ExternalDefinitionKind.KnownValues, external.Kind == ExternalDefinitionKind.Union,
                IsKnownValues: external.Kind == ExternalDefinitionKind.KnownValues);
        var targetPath = resolved.Target.Nsid + "#" + resolved.Target.DefName;
        if (Named(targetPath) is { } decl) return Of(decl);
        if (!IsGenerated(resolved.Target.Nsid) && resolved.Definition is LexObject or LexRecord or LexUnion or LexString { KnownValues: not null } or LexString { Enum: not null })
            Diagnostics.Add(new("ATLEX006", GenerationSeverity.Error, $"{reference} refers to {targetPath}, which this generation excludes.", context.ToString(), path));
        // A named primitive (string without known values, integer, array, …): its own type, inline.
        return TypeOf(resolved.Definition!, resolved.Target.Nsid, targetPath);
    }

    // The $type and C# type of every case of a union, in refs order.
    public IEnumerable<(string Type, CsType Case)> Cases(LexUnion union, Nsid context, string path) =>
        union.Refs.Select(r => LexRefTarget.TryParse(r, context, out var target) && _catalog.TryResolve(target, out var resolved)
            ? (resolved.Target.ToString(), RefType(r, context, path))
            : (r, CsType.Of("global::ATProtoNet.Data.DataModelValue")));

    // The proxy default of a method (LEX-31).
    public string? Proxy(string id) =>
        Naming.MatchesAny(_config.ProxyExclude, id) ? null : _config.Proxies.FirstOrDefault(p => Naming.Matches(p.Key, id)).Value;

    public LexiconCatalog Catalog => _catalog;
    public LexgenConfig Config => _config;
}
