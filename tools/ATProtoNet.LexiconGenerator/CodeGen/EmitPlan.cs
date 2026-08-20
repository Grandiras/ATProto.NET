using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.CodeGen;

/// <summary>
/// Cross-document analysis performed before any C# is written: indexes every definition
/// in the generation run and decides which Lexicon unions can be emitted as a
/// <c>[JsonPolymorphic]</c> base class instead of a raw <c>JsonElement</c>.
/// </summary>
/// <remarks>
/// A union becomes a generated base class only when every one of its refs points at an
/// <c>object</c> definition inside this generation run and no other union already claimed
/// that definition — a C# class can only have one base, so overlapping unions cannot both
/// be modelled by inheritance. Unions that do not qualify fall back to <c>JsonElement?</c>.
/// </remarks>
public sealed class EmitPlan
{
    private readonly Dictionary<string, DefEntry> _defs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnionPlan> _unionsBySignature = new(StringComparer.Ordinal);
    private readonly Dictionary<string, UnionPlan> _basesByRef = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<UnionPlan>> _basesByOwner = new(StringComparer.Ordinal);

    public EmitPlan(IReadOnlyList<LexiconDocument> documents, string namespacePrefix, IList<string> warnings)
    {
        var namesByNamespace = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        // NSID order (not file-enumeration order) so type names are stable across runs.
        foreach (var doc in documents.OrderBy(d => d.Id, StringComparer.Ordinal))
        {
            var ns = TypeMapper.NsidToNamespace(doc.Id, namespacePrefix);
            if (!namesByNamespace.TryGetValue(ns, out var taken))
            {
                taken = new HashSet<string>(StringComparer.Ordinal);
                namesByNamespace[ns] = taken;
            }

            foreach (var (defName, def) in doc.Defs)
            {
                var kind = def.Type;
                var preferred = TypeMapper.DefToClassName(doc.Id, defName, kind);
                var typeName = preferred;

                // Sibling documents share a C# namespace, so identically-named defs
                // (e.g. two "#appPassword" defs under com.atproto.server) must diverge.
                if (taken.Contains(typeName))
                {
                    var lastDot = doc.Id.LastIndexOf('.');
                    var qualifier = TypeMapper.ToPascalCase(lastDot >= 0 ? doc.Id[(lastDot + 1)..] : doc.Id);
                    typeName = UniqueName(qualifier + preferred, taken);
                    warnings.Add($"{doc.Id}#{defName}: '{preferred}' is already used in namespace {ns} — emitted as '{typeName}'");
                }

                taken.Add(typeName);

                _defs[TypeMapper.NormalizeRef($"#{defName}", doc.Id)] = new DefEntry(
                    doc.Id,
                    defName,
                    kind,
                    typeName,
                    ns,
                    def);
            }
        }

        PlanUnions(documents, namespacePrefix, warnings);
    }

    /// <summary>A definition that this generation run emits a C# type for.</summary>
    public sealed record DefEntry(
        string Nsid,
        string DefName,
        string Kind,
        string TypeName,
        string Namespace,
        LexiconSchema Schema)
    {
        /// <summary>Fully-qualified C# name of the generated type.</summary>
        public string QualifiedName => $"{Namespace}.{TypeName}";
    }

    /// <summary>Looks up a definition emitted by this run by its normalized ref.</summary>
    public DefEntry? FindDef(string normalizedRef)
        => _defs.TryGetValue(normalizedRef, out var entry) ? entry : null;

    /// <summary>The union base class planned for a set of normalized refs, if any.</summary>
    public UnionPlan? FindUnion(IEnumerable<string> normalizedRefs)
        => _unionsBySignature.TryGetValue(Signature(normalizedRefs), out var plan) ? plan : null;

    /// <summary>The union base class a generated definition must inherit, if any.</summary>
    public UnionPlan? BaseFor(string normalizedRef)
        => _basesByRef.TryGetValue(normalizedRef, out var plan) ? plan : null;

    /// <summary>The union base classes to emit into the file generated for <paramref name="nsid"/>.</summary>
    public IReadOnlyList<UnionPlan> BasesOwnedBy(string nsid)
        => _basesByOwner.TryGetValue(nsid, out var plans) ? plans : [];

    /// <summary>All generated type names that share a C# namespace (used for collision checks).</summary>
    public IEnumerable<string> TypeNamesIn(string @namespace)
        => _defs.Values.Where(d => d.Namespace == @namespace).Select(d => d.TypeName)
            .Concat(_basesByRef.Values.Where(p => p.Namespace == @namespace).Select(p => p.BaseName))
            .Distinct(StringComparer.Ordinal);

    private static string Signature(IEnumerable<string> normalizedRefs)
        => string.Join("|", normalizedRefs.Distinct(StringComparer.Ordinal).OrderBy(r => r, StringComparer.Ordinal));

    private void PlanUnions(IReadOnlyList<LexiconDocument> documents, string namespacePrefix, IList<string> warnings)
    {
        // Collect union sites in a deterministic order: document order, then definition
        // order, then property order (System.Text.Json preserves JSON member order).
        var sites = new List<UnionSite>();
        foreach (var doc in documents)
        {
            foreach (var (defName, def) in doc.Defs)
            {
                switch (def.Type)
                {
                    case "record":
                        WalkObject(def.Record, doc.Id, defName, sites);
                        break;
                    case "object":
                        WalkObject(def, doc.Id, defName, sites);
                        break;
                    // Other definition types carry no property schemas, so they host no union
                    // sites — a space declaration's fields are all scalars and NSID lists.
                }
            }
        }

        var takenNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var group in sites.GroupBy(s => Signature(s.Refs), StringComparer.Ordinal))
        {
            var signature = group.Key;
            if (_unionsBySignature.ContainsKey(signature))
                continue;

            var refs = group.First().Refs
                .Distinct(StringComparer.Ordinal)
                .OrderBy(r => r, StringComparer.Ordinal)
                .ToList();

            // Every variant has to be an object definition we are generating ourselves —
            // records already extend AtProtoRecord and external types cannot be re-based.
            var variants = refs.Select(FindDef).ToList();
            if (variants.Any(v => v is null || v.Kind != "object"))
                continue;

            // A class has one base: skip a union whose variants another union already claimed.
            if (refs.Any(_basesByRef.ContainsKey))
            {
                warnings.Add(
                    $"union [{string.Join(", ", refs)}] overlaps another union's variants — " +
                    "emitted as JsonElement? instead of a polymorphic base class");
                continue;
            }

            var owner = variants[0]!;
            var ns = owner.Namespace;

            if (!takenNames.TryGetValue(ns, out var taken))
            {
                taken = new HashSet<string>(_defs.Values.Where(d => d.Namespace == ns).Select(d => d.TypeName), StringComparer.Ordinal);
                takenNames[ns] = taken;
            }

            var baseName = UniqueName(TypeMapper.ToIdentifier(TypeMapper.ToPascalCase(group.First().Hint)) + "Union", taken);
            taken.Add(baseName);

            var plan = new UnionPlan
            {
                BaseName = baseName,
                Namespace = ns,
                OwnerNsid = owner.Nsid,
                Refs = refs,
                VariantTypes = refs.ToDictionary(r => r, r => FindDef(r)!.QualifiedName, StringComparer.Ordinal),
            };

            _unionsBySignature[signature] = plan;
            foreach (var r in refs)
                _basesByRef[r] = plan;

            if (!_basesByOwner.TryGetValue(owner.Nsid, out var owned))
            {
                owned = [];
                _basesByOwner[owner.Nsid] = owned;
            }
            owned.Add(plan);
        }
    }

    private static string UniqueName(string preferred, HashSet<string> taken)
    {
        if (!taken.Contains(preferred))
            return preferred;

        for (var i = 2; ; i++)
        {
            var candidate = $"{preferred}{i}";
            if (!taken.Contains(candidate))
                return candidate;
        }
    }

    private static void WalkObject(LexiconSchema? obj, string nsid, string hint, List<UnionSite> sites)
    {
        if (obj?.Properties is null)
            return;

        foreach (var (propName, prop) in obj.Properties)
            WalkSchema(prop, nsid, propName, sites);
    }

    private static void WalkSchema(LexiconSchema schema, string nsid, string hint, List<UnionSite> sites)
    {
        switch (schema.Type)
        {
            case "union" when schema.Refs is { Count: > 1 }:
                sites.Add(new UnionSite(hint, schema.Refs.Select(r => TypeMapper.NormalizeRef(r, nsid)).ToList()));
                break;

            case "array" when schema.Items is not null:
                WalkSchema(schema.Items, nsid, TypeMapper.Singularize(hint), sites);
                break;

            case "object":
                WalkObject(schema, nsid, hint, sites);
                break;
        }
    }

    private sealed record UnionSite(string Hint, List<string> Refs);
}

/// <summary>
/// A Lexicon union modelled as a generated abstract base class with
/// <c>[JsonDerivedType]</c> attributes for each variant.
/// </summary>
public sealed class UnionPlan
{
    /// <summary>Simple C# name of the generated base class (e.g. <c>AttributionUnion</c>).</summary>
    public required string BaseName { get; init; }

    /// <summary>C# namespace the base class is emitted into.</summary>
    public required string Namespace { get; init; }

    /// <summary>NSID of the document whose generated file carries the base class.</summary>
    public required string OwnerNsid { get; init; }

    /// <summary>Normalized refs of the union's variants, sorted.</summary>
    public required List<string> Refs { get; init; }

    /// <summary>Maps each normalized ref to the fully-qualified generated variant type.</summary>
    public required Dictionary<string, string> VariantTypes { get; init; }

    /// <summary>Fully-qualified name of the generated base class.</summary>
    public string QualifiedName => $"{Namespace}.{BaseName}";

    /// <summary>The base class name as written inside <paramref name="currentNamespace"/>.</summary>
    public string NameIn(string currentNamespace)
        => currentNamespace == Namespace ? BaseName : $"global::{QualifiedName}";
}
