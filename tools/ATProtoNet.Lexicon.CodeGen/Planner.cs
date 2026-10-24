using System;
using System.Collections.Generic;
using System.Linq;

namespace ATProtoNet.Lexicon.CodeGen;

// Pass one: names every generated type before anything is written (LEX-32–LEX-37, LEX-89, LEX-90).
internal sealed partial class Planner
{
    private readonly LexiconCatalog _catalog;
    private readonly LexgenConfig _config;
    private readonly IExternalLexiconTypes? _external;
    private readonly Dictionary<string, TypeDecl> _named = new(StringComparer.Ordinal);
    private readonly Dictionary<object, TypeDecl> _inline = new(ReferenceComparer.Instance);
    private readonly Dictionary<string, PlanUnit> _unitOf = new(StringComparer.Ordinal);

    public Planner(LexiconCatalog catalog, LexgenConfig config, IExternalLexiconTypes? external)
    {
        _catalog = catalog;
        _config = config;
        _external = external;
        Units = config.Units.Count == 0 ? [new("Lexicons", ["**"])] : [.. config.Units.Select(u => new PlanUnit(u.Name, u.Include))];
    }

    public List<PlanUnit> Units { get; }
    public List<GenerationDiagnostic> Diagnostics { get; } = [];
    public HashSet<string> UsedRenames { get; } = new(StringComparer.Ordinal);
    public HashSet<string> UsedFieldTypes { get; } = new(StringComparer.Ordinal);
    public HashSet<string> UsedUnionExtensions { get; } = new(StringComparer.Ordinal);

    // The C# type of an open union by its definition path, here or in a referenced assembly (LEX-86).
    public string? UnionType(string path) =>
        AllDecls.FirstOrDefault(d => d.Kind == DeclKind.Union && d.Path == path)?.Ref
        ?? (_external is not null && _external.TryResolve(path, out var name, out var kind) && kind == ExternalTypeKind.Union ? "global::" + name : null);
    public IEnumerable<TypeDecl> AllDecls => Units.SelectMany(u => u.Decls).SelectMany(Flatten);

    public void Plan()
    {
        foreach (var document in _catalog.Documents)
        {
            var id = document.Id.ToString();
            if (_config.Include.Count > 0 && !Naming.MatchesAny(_config.Include, id) || Naming.MatchesAny(_config.Exclude, id)) continue;
            if (Units.FirstOrDefault(u => Naming.MatchesAny(u.Include, id)) is not { } unit) continue;
            unit.Documents.Add(document);
            _unitOf[id] = unit;
            PlanDocument(document, unit);
        }
        CheckNames();
    }

    public bool IsGenerated(Nsid nsid) => _unitOf.ContainsKey(nsid.ToString());

    public PlanUnit? UnitOf(Nsid nsid) => _unitOf.TryGetValue(nsid.ToString(), out var unit) ? unit : null;

    public TypeDecl? Named(string path) => _named.TryGetValue(path, out var decl) ? decl : null;

    public TypeDecl? Inline(LexSchema schema) => _inline.TryGetValue(schema, out var decl) ? decl : null;

    public string Namespace(Nsid nsid) =>
        _config.RootNamespace + string.Concat(nsid.ToString().Split('.').Take(nsid.ToString().Split('.').Length - 1).Select(s => "." + Naming.Pascal(s)));

    public string? Experimental(string id) => _config.Experimental.FirstOrDefault(e => Naming.Matches(e.Key, id)).Value;

    public static IEnumerable<TypeDecl> Flatten(TypeDecl decl) => new[] { decl }.Concat(decl.Nested.SelectMany(Flatten));

    // A rename for a definition path, if configured ("nsid" also names "nsid#main").
    public string Rename(string path, string name)
    {
        if (_config.Renames.TryGetValue(path, out var renamed) || path.EndsWith("#main", StringComparison.Ordinal) && _config.Renames.TryGetValue(path.Substring(0, path.Length - 5), out renamed))
        {
            UsedRenames.Add(_config.Renames.ContainsKey(path) ? path : path.Substring(0, path.Length - 5));
            return renamed;
        }
        return name;
    }

    private TypeDecl Add(DeclKind kind, string name, LexiconDocument document, PlanUnit unit, TypeDecl? parent, string path, LexSchema? schema = null, string? lexiconType = null, LexUnion? union = null)
    {
        var decl = new TypeDecl(kind, Rename(path, name), Namespace(document.Id), parent, document, unit, path)
        {
            Schema = schema,
            LexiconType = lexiconType,
            Union = union,
            Experimental = Experimental(document.Id.ToString()),
        };
        if (parent is null) unit.Decls.Add(decl);
        else parent.Nested.Add(decl);
        return decl;
    }

    private void PlanDocument(LexiconDocument document, PlanUnit unit)
    {
        var id = document.Id.ToString();
        var isDefs = document.Id.Name == "defs";
        var mainName = Naming.Pascal(document.Id.Name);
        TypeDecl? main = null;
        if (!isDefs && document.Main is { } mainDef) main = PlanNamed(document, unit, "main", mainDef, mainName, null);
        var container = main is { Kind: DeclKind.Object or DeclKind.Record or DeclKind.Method } ? main : null;
        TypeDecl? tokens = null;
        foreach (var def in document.Defs)
        {
            if (def.Value is LexToken token)
            {
                tokens ??= Add(DeclKind.Tokens, isDefs ? "Tokens" : mainName + "Tokens", document, unit, null, id + "#tokens");
                tokens.Tokens.Add(new(def.Key, token));
            }
            else if (def.Key != "main" || isDefs)
                PlanNamed(document, unit, def.Key, def.Value, Naming.Pascal(def.Key), isDefs ? null : container);
        }
    }

    private TypeDecl? PlanNamed(LexiconDocument document, PlanUnit unit, string defName, LexUserType def, string name, TypeDecl? parent)
    {
        var id = document.Id.ToString();
        var path = id + "#" + defName;
        var type = defName == "main" ? id : path;
        TypeDecl? decl = def switch
        {
            LexRecord record => Add(DeclKind.Record, name, document, unit, parent, path, record, type),
            LexObject obj => Add(DeclKind.Object, name, document, unit, parent, path, obj, type),
            LexQuery or LexProcedure or LexSubscription => Add(DeclKind.Method, name, document, unit, parent, path, def),
            LexUnion union => Add(DeclKind.Union, name, document, unit, null, path, union, union: union),
            LexString { KnownValues: not null } or LexString { Enum: not null } => Add(DeclKind.KnownValues, name, document, unit, parent, path, def),
            LexPermissionSet => Add(DeclKind.PermissionSet, name, document, unit, parent, path, def),
            LexSpace => Add(DeclKind.Space, name, document, unit, parent, path, def),
            _ => null,
        };
        if (decl is not null) _named[path] = decl;
        switch (def)
        {
            case LexRecord record:
                Walk(decl!, record.Record.Properties, path);
                break;
            case LexObject obj:
                Walk(decl!, obj.Properties, path);
                break;
            case LexArray array:
                InlineFor(parent, defName, array.Items, path, document, unit);
                break;
            case LexQuery query:
                PlanMethod(decl!, query.Parameters, null, query.Output, null);
                break;
            case LexProcedure procedure:
                PlanMethod(decl!, procedure.Parameters, procedure.Input, procedure.Output, null);
                break;
            case LexSubscription subscription:
                PlanMethod(decl!, subscription.Parameters, null, null, subscription.Message);
                break;
        }
        return decl;
    }

    private void PlanMethod(TypeDecl method, LexParams? parameters, LexBody? input, LexBody? output, LexBody? message)
    {
        var id = method.Document.Id.ToString();
        if (parameters is { Properties.Count: > 0 })
        {
            method.Parameters = Add(DeclKind.MethodPart, "Parameters", method.Document, method.Unit, method, id + "#parameters", parameters);
            Walk(method.Parameters, parameters.Properties, id + "#parameters");
        }
        method.Input = Body(method, input?.Schema, "Input", id + "#input");
        method.Output = Body(method, output?.Schema, "Output", id + "#output");
        if (message?.Schema is LexUnion union)
            method.Message = _inline[union] = Add(DeclKind.Union, method.Name + "Message", method.Document, method.Unit, null, id + "#message", union, union: union);
        else method.Message = Body(method, message?.Schema, "Message", id + "#message");
    }

    private TypeDecl? Body(TypeDecl method, LexSchema? schema, string part, string path)
    {
        switch (schema)
        {
            case LexObject obj:
                var decl = Add(DeclKind.MethodPart, part, method.Document, method.Unit, method, path, obj);
                Walk(decl, obj.Properties, path);
                return decl;
            case LexUnion union:
                return _inline[union] = Add(DeclKind.Union, Naming.Join(Naming.OwnerPath([method.Name], true), part), method.Document, method.Unit, null, path, union, union: union);
            default:
                return null;
        }
    }

    private void Walk(TypeDecl container, IReadOnlyDictionary<string, LexSchema> properties, string path)
    {
        foreach (var property in properties) InlineFor(container, property.Key, property.Value, path + "." + property.Key, container.Document, container.Unit);
    }

    // LEX-34: unions at namespace level, known values and inline objects nested (in the method class for method parts).
    private void InlineFor(TypeDecl? container, string property, LexSchema schema, string path, LexiconDocument document, PlanUnit unit)
    {
        var holder = container is { Kind: DeclKind.MethodPart } ? container.Parent : container;
        switch (schema)
        {
            case LexUnion union:
                var owner = Chain(container).ToList();
                _inline[union] = Add(DeclKind.Union, Naming.Join(Naming.OwnerPath(owner.Select(d => d.Name), owner.FirstOrDefault()?.Kind == DeclKind.Method), Naming.Pascal(property)),
                    document, unit, null, path, union, union: union);
                break;
            case LexString { KnownValues: not null } or LexString { Enum: not null }:
                _inline[schema] = Add(DeclKind.KnownValues, Naming.Pascal(property) + "Value", document, unit, holder, path, schema);
                break;
            case LexObject obj:
                var decl = _inline[obj] = Add(DeclKind.Object, Naming.Pascal(property) + "Object", document, unit, holder, path, obj);
                Walk(decl, obj.Properties, path);
                break;
            case LexArray array:
                InlineFor(container, property, array.Items, path, document, unit);
                break;
        }
    }

    private static IEnumerable<TypeDecl> Chain(TypeDecl? decl) => decl is null ? [] : Chain(decl.Parent).Concat([decl]);

    // LEX-37 collisions, LEX-89 name rules, LEX-37 namespace-named types.
    private void CheckNames()
    {
        foreach (var group in AllDecls.GroupBy(d => d.FullName, StringComparer.Ordinal).Where(g => g.Count() > 1))
        {
            var decls = group.ToList();
            Diagnostics.Add(new("ATLEX001", GenerationSeverity.Error,
                $"{string.Join(" and ", decls.Select(d => d.Path))} both map to {group.Key}; add a \"renames\" entry for \"{decls[decls.Count - 1].Path}\".",
                decls[0].Document.Id.ToString(), decls[0].Path));
        }
        foreach (var decl in AllDecls)
        {
            if (decl.Parent is { } parent && parent.Name == decl.Name)
                Diagnostics.Add(new("ATLEX001", GenerationSeverity.Error, $"{decl.Path} is named like its enclosing type {parent.FullName}; add a \"renames\" entry for \"{decl.Path}\".",
                    decl.Document.Id.ToString(), decl.Path));
            if (decl.Kind != DeclKind.Tokens && Naming.Violation(decl.Name) is { } violation)
                Diagnostics.Add(new("ATLEX002", GenerationSeverity.Warning, $"The type name {decl.Name} {violation}; add a \"renames\" entry for \"{decl.Path}\".",
                    decl.Document.Id.ToString(), decl.Path));
            if (decl.Parent is null && decl.Namespace.EndsWith("." + decl.Name, StringComparison.Ordinal))
                Diagnostics.Add(new("ATLEX003", GenerationSeverity.Info, $"{decl.FullName} is named like its namespace; generated code qualifies it, but user code may need to.",
                    decl.Document.Id.ToString(), decl.Path));
        }
    }

    // LEX-90: one sorted line per generated type.
    public string Inventory() =>
        string.Concat(AllDecls.Where(d => d.Kind != DeclKind.MethodPart).Select(d => $"{d.Path} → {d.FullName}\n").OrderBy(l => l, StringComparer.Ordinal));
}
