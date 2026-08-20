using System.Text;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.CodeGen;

/// <summary>
/// Generates C# source code from parsed Lexicon schema documents.
/// Produces classes, records, enums, and client methods that match
/// the hand-written patterns already used in ATProtoNet.Lexicon.
/// </summary>
public sealed class CSharpEmitter
{
    private readonly string _namespacePrefix;
    private readonly List<string> _warnings = [];

    public CSharpEmitter(string namespacePrefix = "ATProtoNet.Lexicon")
    {
        _namespacePrefix = namespacePrefix;
    }

    /// <summary>
    /// Diagnostics collected while emitting — unresolved external refs and unions that
    /// could not be modelled as a base class. Generation still succeeds; the affected
    /// members fall back to <c>JsonElement?</c>.
    /// </summary>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>
    /// Emits C# source files from a Lexicon document.
    /// Returns a list of (relative file path, C# source content) pairs.
    /// </summary>
    /// <remarks>
    /// Refs to definitions in other documents can only be typed when those documents are
    /// part of the same call — prefer <see cref="EmitAll"/> for multi-document schemas.
    /// </remarks>
    public List<(string Path, string Content)> Emit(LexiconDocument doc) => EmitAll([doc]);

    /// <summary>
    /// Emits C# source files for all documents, resolving refs and unions across the whole set.
    /// </summary>
    public List<(string Path, string Content)> EmitAll(IEnumerable<LexiconDocument> documents)
    {
        var docs = documents.Where(d => !string.IsNullOrEmpty(d.Id)).ToList();
        var plan = new EmitPlan(docs, _namespacePrefix, _warnings);

        var results = new List<(string Path, string Content)>();
        foreach (var doc in docs)
        {
            var content = EmitDocument(doc, plan);
            if (content is not null)
                results.Add((GetFileName(doc.Id), content));
        }

        return results;
    }

    /// <summary>Per-file emit state: which usings the generated code ended up needing.</summary>
    private sealed class FileContext
    {
        public required string Nsid { get; init; }
        public required string Namespace { get; init; }
        public required EmitPlan Plan { get; init; }

        public bool NeedsJson { get; set; }
        public bool NeedsSdkModels { get; set; }
        public bool NeedsSdkCore { get; set; }
        public bool NeedsSdkSpaces { get; set; }
    }

    /// <summary>An inline <c>object</c> schema that becomes a nested class.</summary>
    private sealed record PendingNested(string TypeName, LexiconSchema Schema);

    /// <summary>
    /// Every definition type in the Lexicon vocabulary. Not all of them are emitted as a C#
    /// type — the ones that are not are still legitimate input, so only a type missing from
    /// this set earns a warning.
    /// </summary>
    private static readonly HashSet<string> s_knownDefTypes = new(StringComparer.Ordinal)
    {
        "record", "space", "query", "procedure", "subscription",
        "object", "array", "ref", "union", "token",
        "string", "integer", "boolean", "null", "unknown",
        "blob", "bytes", "cid-link",
    };

    private string? EmitDocument(LexiconDocument doc, EmitPlan plan)
    {
        var ctx = new FileContext
        {
            Nsid = doc.Id,
            Namespace = TypeMapper.NsidToNamespace(doc.Id, _namespacePrefix),
            Plan = plan,
        };

        var body = new StringBuilder();
        var hasContent = false;

        // Union base classes are emitted next to their first variant.
        foreach (var union in plan.BasesOwnedBy(doc.Id))
        {
            EmitUnionBase(body, ctx, union);
            hasContent = true;
        }

        var reserved = new HashSet<string>(plan.TypeNamesIn(ctx.Namespace), StringComparer.Ordinal);
        var families = GroupTokens(doc);
        var emittedFamilies = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (defName, def) in doc.Defs)
        {
            switch (def.Type)
            {
                case "record":
                    EmitRecordType(body, ctx, defName, def);
                    hasContent = true;
                    break;

                case "space":
                    EmitSpaceType(body, ctx, defName, def);
                    hasContent = true;
                    break;

                case "object":
                    EmitObjectType(body, ctx, defName, def);
                    hasContent = true;
                    break;

                case "string" when def.Enum is { Count: > 0 }:
                    EmitStringEnum(body, TypeNameOf(ctx, defName, "string"), def);
                    hasContent = true;
                    break;

                case "token":
                    if (families.TryGetValue(defName, out var family))
                    {
                        if (emittedFamilies.Add(family.Key))
                            EmitTokenFamily(body, ctx.Nsid, family, reserved);
                    }
                    else
                    {
                        EmitToken(body, TypeNameOf(ctx, defName, "token"), TypeMapper.TypeValue(ctx.Nsid, defName), def);
                    }
                    hasContent = true;
                    break;

                case "query":
                case "procedure":
                    // Skip XRPC methods for now — these generate client methods separately
                    break;

                case "subscription":
                    // Event streams are complex; skip for initial implementation
                    break;

                default:
                    // Other Lexicon types (string without enum, integer, boolean, etc.) are
                    // referenced as property constraints rather than emitted standalone. A type
                    // outside the vocabulary is a different matter: nothing downstream will ever
                    // pick it up, so say so instead of silently generating an empty file.
                    if (!s_knownDefTypes.Contains(def.Type))
                    {
                        _warnings.Add(def.Type.Length == 0
                            ? $"{ctx.Nsid}#{defName}: definition has no 'type' — nothing was emitted for it"
                            : $"{ctx.Nsid}#{defName}: unsupported definition type '{def.Type}' — nothing was emitted for it");
                    }
                    break;
            }
        }

        if (!hasContent)
            return null;

        var sb = new StringBuilder();
        EmitFileHeader(sb, ctx, doc);
        sb.Append(body);
        return sb.ToString();
    }

    private static void EmitFileHeader(StringBuilder sb, FileContext ctx, LexiconDocument doc)
    {
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine($"// Generated from Lexicon schema: {doc.Id}");
        sb.AppendLine("// Do not edit manually — regenerate with: atproto-lexgen csharp");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("#nullable enable");
        sb.AppendLine();
        sb.AppendLine("using System.Collections.Generic;");
        if (ctx.NeedsJson)
            sb.AppendLine("using System.Text.Json;");
        sb.AppendLine("using System.Text.Json.Serialization;");
        if (ctx.NeedsSdkCore)
            sb.AppendLine("using ATProtoNet;");
        if (ctx.NeedsSdkModels)
            sb.AppendLine("using ATProtoNet.Models;");
        if (ctx.NeedsSdkSpaces)
            sb.AppendLine("using ATProtoNet.Spaces;");
        sb.AppendLine();
        sb.AppendLine($"namespace {ctx.Namespace};");
        sb.AppendLine();
    }

    private void EmitUnionBase(StringBuilder sb, FileContext ctx, UnionPlan union)
    {
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// Base type for the Lexicon union of: {string.Join(", ", union.Refs)}.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("[JsonPolymorphic(TypeDiscriminatorPropertyName = \"$type\")]");

        foreach (var normalizedRef in union.Refs)
        {
            var (nsid, defName) = TypeMapper.SplitRef(normalizedRef);
            var variant = Shorten(union.VariantTypes[normalizedRef], ctx);
            sb.AppendLine($"[JsonDerivedType(typeof({variant}), \"{TypeMapper.TypeValue(nsid, defName)}\")]");
        }

        sb.AppendLine($"public abstract class {union.BaseName} {{ }}");
        sb.AppendLine();
    }

    private void EmitRecordType(StringBuilder sb, FileContext ctx, string defName, LexiconSchema def)
    {
        var className = TypeNameOf(ctx, defName, "record");
        var typeValue = TypeMapper.TypeValue(ctx.Nsid, defName);

        // Generated records subclass the SDK base so they work with RecordCollection<T>.
        ctx.NeedsSdkCore = true;

        EmitXmlDoc(sb, def.Description, 0);
        sb.AppendLine($"public sealed class {className} : AtProtoRecord");
        sb.AppendLine("{");
        sb.AppendLine("    /// <inheritdoc />");
        // The attribute has to be repeated on the override: System.Text.Json does not carry
        // [JsonPropertyName] over from the base declaration, and would emit both
        // "Type" and "$type".
        sb.AppendLine("    [JsonPropertyName(\"$type\")]");
        sb.AppendLine($"    public override string Type => \"{typeValue}\";");

        EmitObjectBody(sb, ctx, className, def.Record, indent: 4, isRecord: true, isFirstMember: false);

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits a space type declaration (<c>"type": "space"</c>) as a static holder around the
    /// SDK's <c>SpaceTypeDeclaration</c>. A space type is not a record — nothing is serialized
    /// against it — so what a consumer needs from it is the NSID it grants on, the consent
    /// name, and the collection set a bare <c>space:</c> scope resolves to.
    /// </summary>
    private void EmitSpaceType(StringBuilder sb, FileContext ctx, string defName, LexiconSchema def)
    {
        var className = TypeNameOf(ctx, defName, "space");
        var typeValue = TypeMapper.TypeValue(ctx.Nsid, defName);

        ctx.NeedsSdkSpaces = true;

        // Key, name, and collections are all required on the declaration. A schema that omits
        // one still has to produce code that compiles, so substitute and say what was missing.
        var key = def.Key;
        if (string.IsNullOrEmpty(key))
        {
            _warnings.Add($"{ctx.Nsid}#{defName}: space declaration has no 'key' — emitted as \"any\"");
            key = "any";
        }

        var name = def.Name;
        if (string.IsNullOrEmpty(name))
        {
            _warnings.Add(
                $"{ctx.Nsid}#{defName}: space declaration has no 'name' — emitted as the raw NSID, " +
                "which is what a consent screen will show");
            name = typeValue;
        }

        var collections = def.Collections ?? [];
        if (collections.Count == 0)
        {
            _warnings.Add(
                $"{ctx.Nsid}#{defName}: space declaration lists no 'collections' — a bare " +
                $"'space:{typeValue}' grant of this type resolves to no writable collections");
        }

        EmitXmlDoc(sb, def.Description, 0);
        sb.AppendLine("/// <remarks>");
        sb.AppendLine($"/// AT Protocol space type <c>{typeValue}</c> — the consent boundary of a");
        sb.AppendLine($"/// <c>space:{typeValue}</c> OAuth scope.");
        sb.AppendLine("/// </remarks>");
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>The space type NSID.</summary>");
        sb.AppendLine($"    public const string Nsid = {Quote(typeValue)};");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The declaration this space type resolves to.</summary>");
        sb.AppendLine("    public static SpaceTypeDeclaration Declaration { get; } = new()");
        sb.AppendLine("    {");

        if (!string.IsNullOrWhiteSpace(def.Description))
            sb.AppendLine($"        Description = {Quote(def.Description)},");

        sb.AppendLine($"        Key = {Quote(key)},");
        sb.AppendLine($"        Name = {Quote(name)},");

        if (def.LocalizedNames is { Count: > 0 })
        {
            sb.AppendLine("        LocalizedNames = new Dictionary<string, string>");
            sb.AppendLine("        {");
            foreach (var (language, localized) in def.LocalizedNames)
                sb.AppendLine($"            [{Quote(language)}] = {Quote(localized)},");
            sb.AppendLine("        },");
        }

        if (collections.Count == 0)
        {
            sb.AppendLine("        Collections = [],");
        }
        else
        {
            sb.AppendLine("        Collections =");
            sb.AppendLine("        [");
            foreach (var collection in collections)
                sb.AppendLine($"            {Quote(collection)},");
            sb.AppendLine("        ],");
        }

        sb.AppendLine("    };");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The recommended space key type (e.g. <c>tid</c>, <c>any</c>, <c>literal:self</c>).</summary>");
        sb.AppendLine("    public static string Key => Declaration.Key;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The human-readable name shown to users on OAuth consent screens.</summary>");
        sb.AppendLine("    public static string Name => Declaration.Name;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>Localized <see cref=\"Name\"/> values, keyed by language code.</summary>");
        sb.AppendLine("    public static IReadOnlyDictionary<string, string>? LocalizedNames => Declaration.LocalizedNames;");
        sb.AppendLine();
        sb.AppendLine("    /// <summary>The default collection set for a bare <c>space:</c> grant of this type.</summary>");
        sb.AppendLine("    public static IReadOnlyList<string> Collections => Declaration.Collections;");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>Renders a string as a C# literal. Space names are free text, unlike an NSID.</summary>
    private static string Quote(string? value)
    {
        if (value is null)
            return "null";

        var sb = new StringBuilder("\"");
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\r': sb.Append("\\r"); break;
                case '\n': sb.Append("\\n"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(ch); break;
            }
        }

        return sb.Append('"').ToString();
    }

    private void EmitObjectType(StringBuilder sb, FileContext ctx, string defName, LexiconSchema def)
    {
        var className = TypeNameOf(ctx, defName, "object");
        var union = ctx.Plan.BaseFor(TypeMapper.NormalizeRef($"#{defName}", ctx.Nsid));
        var baseClause = union is null ? "" : $" : {union.NameIn(ctx.Namespace)}";

        EmitXmlDoc(sb, def.Description, 0);
        sb.AppendLine($"public sealed class {className}{baseClause}");
        sb.AppendLine("{");

        EmitObjectBody(sb, ctx, className, def, indent: 4, isRecord: false, isFirstMember: true);

        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits the members of an object schema, plus any nested classes generated for
    /// inline <c>object</c> schemas found on its properties.
    /// </summary>
    private void EmitObjectBody(
        StringBuilder sb,
        FileContext ctx,
        string typeName,
        LexiconSchema? objectSchema,
        int indent,
        bool isRecord,
        bool isFirstMember)
    {
        var pad = new string(' ', indent);

        // A member may not repeat the name of its enclosing type (CS0542) or of another member.
        var used = new HashSet<string>(StringComparer.Ordinal) { typeName };
        if (isRecord)
        {
            used.Add("Type");
            used.Add("CreatedAt");
        }

        var nested = new List<PendingNested>();
        var requiredSet = objectSchema?.Required is not null
            ? new HashSet<string>(objectSchema.Required, StringComparer.Ordinal)
            : [];

        var properties = objectSchema?.Properties ?? new Dictionary<string, LexiconSchema>();

        foreach (var (propName, propSchema) in properties)
        {
            // AtProtoRecord already carries createdAt.
            if (isRecord && propName == "createdAt" && propSchema.Type == "string")
                continue;

            var memberName = MemberName(propName, propSchema, used);
            used.Add(memberName);

            var csharpType = ResolveType(propSchema, ctx, propName, used, nested);

            if (!isFirstMember)
                sb.AppendLine();
            isFirstMember = false;

            EmitXmlDoc(sb, propSchema.Description, indent);
            EmitConstraintComments(sb, propSchema, indent);
            sb.AppendLine($"{pad}[JsonPropertyName(\"{propName}\")]");

            if (requiredSet.Contains(propName))
            {
                sb.AppendLine($"{pad}public required {csharpType} {memberName} {{ get; init; }}");
            }
            else
            {
                // Optional members are nullable — a bare JsonElement would serialize as
                // ValueKind.Undefined and throw.
                var nullableType = csharpType.EndsWith('?') ? csharpType : $"{csharpType}?";
                sb.AppendLine($"{pad}public {nullableType} {memberName} {{ get; init; }}");
            }
        }

        // Nested classes for inline object schemas (recursive — an inline object may hold more).
        foreach (var pending in nested)
        {
            sb.AppendLine();
            EmitXmlDoc(sb, pending.Schema.Description, indent);
            sb.AppendLine($"{pad}public sealed class {pending.TypeName}");
            sb.AppendLine($"{pad}{{");
            EmitObjectBody(sb, ctx, pending.TypeName, pending.Schema, indent + 4, isRecord: false, isFirstMember: true);
            sb.AppendLine($"{pad}}}");
        }
    }

    /// <summary>
    /// Maps a property schema to a C# type, resolving refs against the generation run and
    /// the SDK's own models, and queueing nested classes for inline object schemas.
    /// </summary>
    private string ResolveType(
        LexiconSchema schema,
        FileContext ctx,
        string hint,
        HashSet<string> used,
        List<PendingNested> nested)
    {
        switch (schema.Type)
        {
            case "ref" when schema.Ref is not null:
                return ResolveRefType(schema.Ref, ctx.Nsid, ctx);

            case "union" when schema.Refs is { Count: > 0 }:
            {
                var refs = schema.Refs.Select(r => TypeMapper.NormalizeRef(r, ctx.Nsid)).ToList();

                // A union of tokens (or of string defs) carries the token NSID as a string —
                // the generated token families are the values to assign.
                if (refs.All(r => ctx.Plan.FindDef(r) is { Kind: "token" or "string" }))
                    return "string";

                // A single-variant union is just that type.
                if (refs.Count == 1)
                    return ResolveRefType(schema.Refs[0], ctx.Nsid, ctx);

                var union = ctx.Plan.FindUnion(refs);
                if (union is not null)
                    return union.NameIn(ctx.Namespace);

                return Fallback(ctx);
            }

            case "array":
            {
                var itemType = schema.Items is null
                    ? Fallback(ctx)
                    : ResolveType(schema.Items, ctx, TypeMapper.Singularize(hint), used, nested);
                return $"List<{itemType}>";
            }

            case "object" when schema.Properties is { Count: > 0 }:
            {
                var typeName = UniqueName(TypeMapper.ToIdentifier(TypeMapper.ToPascalCase(hint)), used, "Info");
                used.Add(typeName);
                nested.Add(new PendingNested(typeName, schema));
                return typeName;
            }

            case "blob":
                ctx.NeedsSdkModels = true;
                return "BlobRef";

            default:
            {
                var mapped = TypeMapper.GetCSharpType(schema, ctx.Nsid, _namespacePrefix);
                if (mapped.Contains("JsonElement", StringComparison.Ordinal))
                    ctx.NeedsJson = true;
                return mapped;
            }
        }
    }

    /// <summary>
    /// Resolves a <c>ref</c> to a C# type: a definition from this generation run, an SDK
    /// model, or — when neither is available — <c>JsonElement</c> plus a warning, because a
    /// guessed type name would not compile.
    /// </summary>
    private string ResolveRefType(string refString, string contextNsid, FileContext ctx, int depth = 0)
    {
        var normalized = TypeMapper.NormalizeRef(refString, contextNsid);

        var def = ctx.Plan.FindDef(normalized);
        if (def is not null)
        {
            switch (def.Kind)
            {
                case "record":
                case "object":
                    return Shorten(def.QualifiedName, ctx);

                // Tokens and string defs are emitted as static constant holders, so the
                // member itself is a plain string. So is a space type: nothing is serialized
                // against the declaration, so a property pointing at one carries its NSID.
                case "token":
                case "string":
                case "space":
                    return "string";

                case "ref" when def.Schema.Ref is not null && depth < 4:
                    return ResolveRefType(def.Schema.Ref, def.Nsid, ctx, depth + 1);

                default:
                {
                    var mapped = TypeMapper.GetCSharpType(def.Schema, def.Nsid, _namespacePrefix);
                    if (mapped.Contains("JsonElement", StringComparison.Ordinal))
                        ctx.NeedsJson = true;
                    if (mapped.Contains("BlobRef", StringComparison.Ordinal))
                        ctx.NeedsSdkModels = true;
                    return mapped;
                }
            }
        }

        if (SdkTypeMap.TryResolve(normalized, out var sdkType))
            return Shorten(sdkType, ctx);

        var (nsid, _) = TypeMapper.SplitRef(normalized);
        _warnings.Add(SdkTypeMap.IsSdkAuthority(nsid)
            ? $"{ctx.Nsid}: ref '{refString}' has no ATProtoNet model — emitted as JsonElement?"
            : $"{ctx.Nsid}: ref '{refString}' was not part of this generation run — emitted as JsonElement? " +
              "(include its schema in --input to get a typed member)");

        return Fallback(ctx);
    }

    private static string Fallback(FileContext ctx)
    {
        ctx.NeedsJson = true;
        return "JsonElement";
    }

    /// <summary>
    /// The C# type name the plan assigned to a definition of the file's own document.
    /// </summary>
    private static string TypeNameOf(FileContext ctx, string defName, string kind)
        => ctx.Plan.FindDef(TypeMapper.NormalizeRef($"#{defName}", ctx.Nsid))?.TypeName
           ?? TypeMapper.DefToClassName(ctx.Nsid, defName, kind);

    /// <summary>
    /// Drops the namespace qualifier when the type lives in the file's own namespace, and
    /// otherwise roots the reference at <c>global::</c> — a generated namespace such as
    /// <c>Bsky.Generated.Chat.Bsky.Actor</c> would otherwise resolve <c>Bsky.…</c> to itself.
    /// </summary>
    private static string Shorten(string qualifiedName, FileContext ctx)
    {
        var lastDot = qualifiedName.LastIndexOf('.');
        if (lastDot < 0)
            return qualifiedName;

        return qualifiedName[..lastDot] == ctx.Namespace
            ? qualifiedName[(lastDot + 1)..]
            : $"global::{qualifiedName}";
    }

    /// <summary>
    /// Picks a legal, non-colliding C# member name for a Lexicon property. The JSON name is
    /// preserved by <c>[JsonPropertyName]</c>, so renaming is wire-compatible.
    /// </summary>
    private static string MemberName(string propName, LexiconSchema schema, HashSet<string> used)
    {
        var pascal = TypeMapper.ToIdentifier(TypeMapper.ToPascalCase(propName));

        // e.g. a blob property "image" inside the object def "image".
        var suffix = schema.Type switch
        {
            "blob" => "Blob",
            "ref" or "union" => "Ref",
            "array" => "List",
            _ => "Value",
        };

        return UniqueName(pascal, used, suffix);
    }

    private static string UniqueName(string preferred, HashSet<string> used, string suffix)
    {
        if (!used.Contains(preferred))
            return preferred;

        var withSuffix = preferred + suffix;
        if (!used.Contains(withSuffix))
            return withSuffix;

        for (var i = 2; ; i++)
        {
            var candidate = $"{preferred}{i}";
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    private static void EmitStringEnum(StringBuilder sb, string enumName, LexiconSchema def)
    {
        EmitXmlDoc(sb, def.Description, 0);
        sb.AppendLine("/// <remarks>");
        sb.AppendLine("/// Serialized as a string. Use the string constants in this static class");
        sb.AppendLine("/// rather than a C# enum to preserve AT Protocol wire compatibility.");
        sb.AppendLine("/// </remarks>");
        sb.AppendLine($"public static class {enumName}");
        sb.AppendLine("{");

        var used = new HashSet<string>(StringComparer.Ordinal) { enumName };
        foreach (var value in def.Enum!)
        {
            var constName = UniqueName(TypeMapper.ToIdentifier(TypeMapper.ToPascalCase(value)), used, "Value");
            used.Add(constName);
            sb.AppendLine($"    public const string {constName} = \"{value}\";");
        }

        sb.AppendLine("}");
        sb.AppendLine();
    }

    private static void EmitToken(StringBuilder sb, string typeName, string tokenValue, LexiconSchema def)
    {
        EmitXmlDoc(sb, def.Description, 0);
        sb.AppendLine("/// <remarks>AT Protocol token type — use the <see cref=\"Value\"/> constant.</remarks>");
        sb.AppendLine($"public static class {typeName}");
        sb.AppendLine("{");
        sb.AppendLine($"    public const string Value = \"{tokenValue}\";");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>
    /// Emits one static class per token family — a Lexicon that declares 40 <c>cookingMethod*</c>
    /// tokens gets a single <c>CookingMethod</c> class with 40 constants instead of 40 classes.
    /// </summary>
    private static void EmitTokenFamily(StringBuilder sb, string nsid, TokenFamily family, HashSet<string> reserved)
    {
        var className = UniqueName(family.Name, reserved, "Tokens");
        reserved.Add(className);

        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// AT Protocol token family <c>{family.Prefix}*</c> from <c>{nsid}</c>.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine($"public static class {className}");
        sb.AppendLine("{");

        var used = new HashSet<string>(StringComparer.Ordinal) { className, "All" };
        var members = new List<(string Name, string Value)>();

        var isFirst = true;
        foreach (var (defName, memberName, def) in family.Members)
        {
            var name = UniqueName(memberName, used, "Token");
            used.Add(name);
            var value = TypeMapper.TypeValue(nsid, defName);
            members.Add((name, value));

            if (!isFirst)
                sb.AppendLine();
            isFirst = false;

            EmitXmlDoc(sb, def.Description, 4);
            sb.AppendLine($"    public const string {name} = \"{value}\";");
        }

        sb.AppendLine();
        sb.AppendLine("    /// <summary>Every token in this family.</summary>");
        sb.AppendLine($"    public static readonly IReadOnlyList<string> All = new[] {{ {string.Join(", ", members.Select(m => m.Name))} }};");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    /// <summary>A set of tokens sharing a leading camel-case prefix (e.g. <c>cookingMethod*</c>).</summary>
    private sealed record TokenFamily(
        string Key,
        string Prefix,
        string Name,
        List<(string DefName, string MemberName, LexiconSchema Def)> Members);

    /// <summary>
    /// Groups a document's token definitions into families: tokens sharing a first word are
    /// grouped, and the family name is their longest common camel-case word prefix — so
    /// <c>cookingMethodBaking</c>/<c>cookingMethodFrying</c> land in <c>CookingMethod</c>
    /// while <c>licenseAllRights</c>/<c>licenseCreativeCommonsBy</c> land in <c>License</c>.
    /// Tokens without a shared first word keep their own static class.
    /// </summary>
    private static Dictionary<string, TokenFamily> GroupTokens(LexiconDocument doc)
    {
        var tokens = doc.Defs
            .Where(kv => kv.Value.Type == "token")
            .Select(kv => (DefName: kv.Key, Words: TypeMapper.SplitWords(kv.Key), Def: kv.Value))
            // A single-word token has no remainder left to name a constant after.
            .Where(t => t.Words.Count > 1)
            .ToList();

        var byDefName = new Dictionary<string, TokenFamily>(StringComparer.Ordinal);

        foreach (var group in tokens.GroupBy(t => t.Words[0], StringComparer.Ordinal))
        {
            var members = group.ToList();
            if (members.Count < 2)
                continue;

            // Extend the shared prefix as far as all members agree, always leaving at
            // least one word to name the constant after.
            var cap = members.Min(m => m.Words.Count) - 1;
            var prefixLength = 1;
            while (prefixLength < cap
                   && members.All(m => string.Equals(m.Words[prefixLength], members[0].Words[prefixLength], StringComparison.Ordinal)))
            {
                prefixLength++;
            }

            var prefix = string.Concat(members[0].Words.Take(prefixLength));
            var family = new TokenFamily(
                prefix,
                prefix,
                TypeMapper.ToIdentifier(TypeMapper.ToPascalCase(prefix)),
                []);

            foreach (var member in members)
            {
                var memberName = TypeMapper.ToIdentifier(
                    TypeMapper.ToPascalCase(string.Concat(member.Words.Skip(prefixLength))));
                family.Members.Add((member.DefName, memberName, member.Def));
                byDefName[member.DefName] = family;
            }
        }

        return byDefName;
    }

    private static void EmitXmlDoc(StringBuilder sb, string? description, int indent)
    {
        if (string.IsNullOrWhiteSpace(description))
            return;

        var prefix = new string(' ', indent);

        // Escape XML special characters
        var escaped = description
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

        sb.AppendLine($"{prefix}/// <summary>");
        foreach (var line in escaped.Split('\n'))
            sb.AppendLine($"{prefix}/// {line.TrimEnd('\r')}");
        sb.AppendLine($"{prefix}/// </summary>");
    }

    private static void EmitConstraintComments(StringBuilder sb, LexiconSchema schema, int indent)
    {
        var pad = new string(' ', indent);
        var constraints = new List<string>();

        if (schema.MinLength is not null) constraints.Add($"minLength: {schema.MinLength}");
        if (schema.MaxLength is not null) constraints.Add($"maxLength: {schema.MaxLength}");
        if (schema.MinGraphemes is not null) constraints.Add($"minGraphemes: {schema.MinGraphemes}");
        if (schema.MaxGraphemes is not null) constraints.Add($"maxGraphemes: {schema.MaxGraphemes}");
        if (schema.Minimum is not null) constraints.Add($"minimum: {schema.Minimum}");
        if (schema.Maximum is not null) constraints.Add($"maximum: {schema.Maximum}");
        if (schema.Format is not null) constraints.Add($"format: {schema.Format}");
        if (schema.Default is not null) constraints.Add($"default: {schema.Default}");

        if (constraints.Count > 0)
            sb.AppendLine($"{pad}// Constraints: {string.Join(", ", constraints)}");

        if (schema.KnownValues is { Count: > 0 })
            sb.AppendLine($"{pad}// Known values: {string.Join(", ", schema.KnownValues)}");

        if (schema.Type == "union" && schema.Refs is { Count: > 0 })
            sb.AppendLine($"{pad}// Union of: {string.Join(", ", schema.Refs)}");
    }

    private static string GetFileName(string nsid)
    {
        var lastDot = nsid.LastIndexOf('.');
        var authority = lastDot >= 0 ? nsid[..lastDot] : nsid;
        var name = lastDot >= 0 ? nsid[(lastDot + 1)..] : nsid;

        var segments = authority.Split('.');
        var path = string.Join("/", segments.Select(TypeMapper.ToNamespaceSegment));
        var fileName = TypeMapper.ToPascalCase(name);

        return $"{path}/{fileName}.g.cs";
    }
}
