using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Lexicon.Upstream;

/// <summary>
/// Checks the SDK's hand-written Lexicon surface against the vendored upstream snapshot
/// (<c>Lexicon/Upstream/README.md</c>): the methods it calls, the JSON names its models use,
/// the permission sets it names and its knownValues constants.
/// </summary>
/// <remarks>
/// A failure means either the SDK drifted from the Lexicons, or the snapshot was refreshed and
/// upstream changed. Fix the model, or, for a deliberate difference, add a commented entry to the
/// matching allow-list in <c>LexiconDriftTests.Allowlists.cs</c>. Every entry must still match
/// something (<see cref="Allowlists_AllStillMatch"/>), so none can linger.
/// </remarks>
public partial class LexiconDriftTests
{
    private static UpstreamLexicons Upstream => UpstreamLexicons.Instance;

    /// <summary>
    /// NSIDs the SDK uses that the snapshot deliberately lacks, by prefix. Permissioned data
    /// (<c>com.atproto.space.*</c>, <c>com.atproto.simplespace.*</c>) is a proposal that is not on
    /// upstream <c>main</c> yet.
    /// </summary>
    private static readonly string[] NotUpstream = ["com.atproto.space.", "com.atproto.simplespace."];

    // ──────────────────────────────────────────────────────────
    //  Methods
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void XrpcCalls_ExistUpstreamWithTheirHttpMethod()
    {
        var calls = SdkSource.XrpcCalls;
        Assert.True(calls.Count > 150, $"Only {calls.Count} XRPC call sites found; is the scan still matching?");

        var failures = new List<string>();
        foreach (var call in calls.Where(c => !IsNotUpstream(c.Nsid)))
        {
            var kind = Upstream.KindOf(call.Nsid);
            var expected = call.IsGet ? "query" : "procedure";
            if (kind is null)
                failures.Add($"{call.Nsid}: not an upstream Lexicon ({call.Location})");
            else if (kind != expected)
                failures.Add($"{call.Nsid}: upstream is a {kind}, the SDK sends {(call.IsGet ? "GET" : "POST")} ({call.Location})");
        }

        Assert.True(failures.Count == 0, "XRPC calls that do not match upstream:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void NsidLiterals_NameUpstreamDefs()
    {
        Assert.True(SdkSource.NsidLiterals.Count > 250, "Too few NSID literals found; is the scan still matching?");

        var failures = SdkSource.NsidLiterals
            .Where(l => !IsNotUpstream(l.Value) && !Upstream.TryGetDef(l.Value, out _))
            .Select(l => $"{l.Value} ({l.Location})")
            .Distinct()
            .ToList();

        Assert.True(
            failures.Count == 0,
            "These NSIDs and def references in src/ name nothing in the upstream snapshot:\n  " +
            string.Join("\n  ", failures));
    }

    // ──────────────────────────────────────────────────────────
    //  Models
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void LexiconModels_EachResolveToAnUpstreamObject()
    {
        var failures = new List<string>();
        foreach (var type in LexiconModels.Where(t => !NotLexiconObjects.ContainsKey(t)))
        {
            var reference = ResolveDef(type);
            if (reference is null)
                failures.Add($"{Name(type)}: no upstream def found; add it to DefOf or NotLexiconObjects");
            else if (!Upstream.TryGetObject(reference, out _))
                failures.Add($"{Name(type)}: '{reference}' is not an upstream object schema");
        }

        Assert.True(failures.Count == 0, "Lexicon models without an upstream object:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void LexiconModels_JsonPropertyNamesExistOnTheirUpstreamDef()
    {
        var failures = ResolvedModels()
            .SelectMany(m => JsonNames(m.Type)
                .Where(n => !m.Properties.Contains(n) && !UnknownProperties.ContainsKey($"{Name(m.Type)}.{n}"))
                .Select(n => $"{Name(m.Type)}.{n}: not a property of {m.Reference}"))
            .ToList();

        Assert.True(failures.Count == 0, "JSON names that upstream does not define:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void LexiconModels_DeclareEveryPropertyOfTheirUpstreamDef()
    {
        var failures = ResolvedModels()
            .SelectMany(m =>
            {
                var declared = JsonNames(m.Type).ToHashSet(StringComparer.Ordinal);
                return m.Properties
                    .Where(p => !declared.Contains(p) && !OmittedProperties.ContainsKey($"{Name(m.Type)}.{p}"))
                    .Select(p => $"{Name(m.Type)}.{p}: declared by {m.Reference}");
            })
            .ToList();

        Assert.True(failures.Count == 0, "Upstream properties the models lack:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void Allowlists_AllStillMatch()
    {
        var stale = new List<string>();
        var resolved = ResolvedModels().ToList();

        foreach (var key in UnknownProperties.Keys)
        {
            if (!resolved.Any(m => JsonNames(m.Type).Any(n => $"{Name(m.Type)}.{n}" == key && !m.Properties.Contains(n))))
                stale.Add($"UnknownProperties[{key}]");
        }

        foreach (var key in OmittedProperties.Keys)
        {
            if (!resolved.Any(m => m.Properties.Any(p => $"{Name(m.Type)}.{p}" == key && !JsonNames(m.Type).Contains(p))))
                stale.Add($"OmittedProperties[{key}]");
        }

        foreach (var (type, reference) in DefOf)
        {
            if (!LexiconModels.Contains(type))
                stale.Add($"DefOf[{Name(type)}]: not a Lexicon model");
            else if (ResolveByConvention(type) == reference)
                stale.Add($"DefOf[{Name(type)}]: resolves to {reference} without the entry");
        }

        foreach (var type in NotLexiconObjects.Keys.Where(t => !LexiconModels.Contains(t)))
            stale.Add($"NotLexiconObjects[{Name(type)}]: not a Lexicon model");

        foreach (var key in ExtraKnownValues.Keys)
        {
            var match = KnownValueLocations.Any(c =>
                KnownValueConstants(c.Class).Any(v => $"{ShortName(c.Class)}:{v}" == key)
                && !KnownValuesAt(c.Location).Contains(key[(key.IndexOf(':') + 1)..]));
            if (!match)
                stale.Add($"ExtraKnownValues[{key}]");
        }

        foreach (var key in MissingKnownValues.Keys)
        {
            var match = KnownValueLocations.Any(c =>
                KnownValuesAt(c.Location).Any(v =>
                    !KnownValueConstants(c.Class).Contains(v) && Matches(key, $"{ShortName(c.Class)}:{v}")));
            if (!match)
                stale.Add($"MissingKnownValues[{key}]");
        }

        Assert.True(stale.Count == 0, "Remove these stale allow-list entries:\n  " + string.Join("\n  ", stale));
    }

    // ──────────────────────────────────────────────────────────
    //  Permission sets and knownValues
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void PermissionSetConstants_ArePublishedPermissionSets()
    {
        var failures = PermissionSetConstants()
            .Where(c => Upstream.KindOf(c.Value) != "permission-set")
            .Select(c => $"{c.Name} = {c.Value}")
            .ToList();

        Assert.True(failures.Count == 0, "Permission sets that are not published:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void PublishedPermissionSets_EachHaveAConstant()
    {
        var constants = PermissionSetConstants().Select(c => c.Value).ToHashSet(StringComparer.Ordinal);
        var missing = Upstream.Defs
            .Where(d => d.Def.GetProperty("type").GetString() == "permission-set")
            .Select(d => d.Reference)
            .Where(nsid => !constants.Contains(nsid))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0, "Add these to AtProtoScopes.PermissionSets:\n  " + string.Join("\n  ", missing));
    }

    [Theory]
    [MemberData(nameof(KnownValueClasses))]
    public void KnownValueConstants_MatchUpstream(string constantsClass, string location)
    {
        var upstream = KnownValuesAt(location);
        var all = KnownValueConstants(constantsClass, includeObsolete: true);
        var current = KnownValueConstants(constantsClass)
            .Where(v => !ExtraKnownValues.ContainsKey($"{ShortName(constantsClass)}:{v}"));

        var missing = upstream
            .Where(v => !all.Contains(v) && !MissingKnownValues.Keys.Any(k => Matches(k, $"{ShortName(constantsClass)}:{v}")))
            .Order(StringComparer.Ordinal)
            .ToList();
        var extra = current.Where(v => !upstream.Contains(v)).Order(StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0, $"Add these upstream values: {string.Join(", ", missing)}");
        Assert.True(extra.Count == 0, $"Upstream does not list these values: {string.Join(", ", extra)}");
    }

    // ──────────────────────────────────────────────────────────
    //  Model resolution
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Every type in the Lexicon namespaces, and <c>ATProtoNet.Models</c>, that declares a JSON
    /// property: the SDK's wire models, internal request and response bodies included.
    /// </summary>
    private static readonly HashSet<Type> LexiconModels = typeof(AtProtoClient).Assembly.GetTypes()
        .Where(t => t.Namespace is { } ns
            && (ns.StartsWith("ATProtoNet.Lexicon.", StringComparison.Ordinal) || ns == "ATProtoNet.Models")
            && !IsNotUpstream(NsidPrefix(t) + ".")
            && !t.IsDefined(typeof(CompilerGeneratedAttribute))
            && JsonNames(t).Any())
        .ToHashSet();

    private static IEnumerable<(Type Type, string Reference, IReadOnlySet<string> Properties)> ResolvedModels()
    {
        foreach (var type in LexiconModels.Where(t => !NotLexiconObjects.ContainsKey(t)))
        {
            if (ResolveDef(type) is { } reference && Upstream.TryGetObject(reference, out var schema))
                yield return (type, reference, UpstreamLexicons.PropertiesOf(schema));
        }
    }

    private static string? ResolveDef(Type type) =>
        DefOf.TryGetValue(type, out var reference) ? reference : ResolveByConvention(type);

    /// <summary>
    /// Finds a model's def without an explicit entry: the <c>$type</c> it writes, the discriminator
    /// its union declares for it, or its name: <c>FooResponse</c> and <c>FooRequest</c> are the
    /// output and input of method <c>foo</c>, <c>FooRecord</c> is record <c>foo</c>, and any other
    /// <c>Foo</c> is the one def named <c>foo</c> in the type's namespace.
    /// </summary>
    private static string? ResolveByConvention(Type type)
    {
        if (!type.IsAbstract
            && type.GetProperty("Type") is { } typeProperty
            && typeProperty.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name == "$type"
            && typeProperty.PropertyType == typeof(string)
            && typeProperty.GetValue(RuntimeHelpers.GetUninitializedObject(type)) is string written
            && Upstream.TryGetDef(written, out _))
        {
            return written;
        }

        for (var baseType = type.BaseType; baseType is not null; baseType = baseType.BaseType)
        {
            if (baseType.GetCustomAttributes<JsonDerivedTypeAttribute>()
                    .FirstOrDefault(a => a.DerivedType == type)?.TypeDiscriminator is string discriminator)
            {
                return discriminator;
            }
        }

        var prefix = NsidPrefix(type);
        if (prefix is null)
            return null;

        var name = type.Name;
        foreach (var (suffix, part) in new[] { ("Response", "#output"), ("Request", "#input"), ("Record", "") })
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                var nsid = $"{prefix}.{Camel(name[..^suffix.Length])}";
                var kind = Upstream.KindOf(nsid);
                if (part == "" ? kind == "record" : kind is "query" or "procedure")
                    return nsid + part;
            }
        }

        var candidates = Upstream.Defs
            .Select(d => d.Reference)
            .Where(r => r.StartsWith(prefix + ".", StringComparison.Ordinal) || r.StartsWith(prefix + "#", StringComparison.Ordinal))
            .Where(r => r.Contains('#') ? r[(r.IndexOf('#') + 1)..] == Camel(name) : r[(r.LastIndexOf('.') + 1)..] == Camel(name))
            .ToList();

        return candidates.Count == 1 ? candidates[0] : null;
    }

    /// <summary>
    /// The NSID prefix of a type's namespace: <c>ATProtoNet.Lexicon.App.Bsky.Feed</c> is
    /// <c>app.bsky.feed</c>.
    /// </summary>
    private static string? NsidPrefix(Type type) =>
        type.Namespace!.StartsWith("ATProtoNet.Lexicon.", StringComparison.Ordinal)
            ? type.Namespace["ATProtoNet.Lexicon.".Length..].ToLowerInvariant()
            : null;

    private static IEnumerable<string> JsonNames(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .OfType<string>()
            .Where(n => n != "$type");

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];

    private static string Name(Type type) => type.FullName!["ATProtoNet.".Length..];

    private static string ShortName(string constantsClass) => constantsClass[(constantsClass.LastIndexOf('.') + 1)..];

    private static bool IsNotUpstream(string nsid) =>
        NotUpstream.Any(p => nsid.StartsWith(p, StringComparison.Ordinal));

    private static bool Matches(string allowListKey, string key) =>
        allowListKey.EndsWith('*')
            ? key.StartsWith(allowListKey[..^1], StringComparison.Ordinal)
            : key == allowListKey;

    private static IEnumerable<(string Name, string Value)> PermissionSetConstants() =>
        typeof(AtProtoScopes.PermissionSets).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string) && f.GetCustomAttribute<ObsoleteAttribute>() is null)
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!));

    private static HashSet<string> KnownValueConstants(string constantsClass, bool includeObsolete = false) =>
        typeof(AtProtoClient).Assembly.GetType(constantsClass, throwOnError: true)!
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string)
                && (includeObsolete || f.GetCustomAttribute<ObsoleteAttribute>() is null))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The knownValues at a location: <c>reference</c> for a string def, or
    /// <c>reference:property</c> for a property of an object (its items, for an array).
    /// </summary>
    private static HashSet<string> KnownValuesAt(string location)
    {
        var colon = location.IndexOf(':', StringComparison.Ordinal);
        JsonElement node;
        if (colon < 0)
        {
            Assert.True(Upstream.TryGetDef(location, out node), $"{location} is not an upstream def");
        }
        else
        {
            Assert.True(Upstream.TryGetObject(location[..colon], out var schema), $"{location[..colon]} is not an upstream object");
            node = schema.GetProperty("properties").GetProperty(location[(colon + 1)..]);
        }

        if (node.GetProperty("type").GetString() == "array")
            node = node.GetProperty("items");

        var values = node.TryGetProperty("knownValues", out var known) ? known : node.GetProperty("enum");
        return values.EnumerateArray().Select(v => v.GetString()!).ToHashSet(StringComparer.Ordinal);
    }
}
