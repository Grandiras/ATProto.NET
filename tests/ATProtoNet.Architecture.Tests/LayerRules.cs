namespace ATProtoNet.Architecture.Tests;

/// <summary>
/// The dependency rules: references point down the layers, areas form no cycles, Lexicon families
/// follow the Lexicon reference graph, and layer 0 does no I/O and reads no clock.
/// </summary>
public static class LayerRules
{
    private static readonly string[] IoTypePrefixes =
    [
        "System.Net.Http.", "System.Net.Sockets.", "System.Net.WebSockets.", "System.Net.Dns",
        "System.IO.File", "System.IO.Directory", "System.IO.Pipes.", "System.IO.MemoryMappedFiles.",
        "System.Diagnostics.Stopwatch", "System.Threading.Timer",
    ];

    private static readonly string[] ClockMembers =
    [
        "System.DateTime::get_Now", "System.DateTime::get_UtcNow", "System.DateTimeOffset::get_Now",
        "System.DateTimeOffset::get_UtcNow", "System.TimeProvider::get_System", "System.Environment::get_TickCount",
        "System.Environment::get_TickCount64",
    ];

    /// <summary>Every rule violation of <paramref name="usages"/> under <paramref name="layers"/>, as messages.</summary>
    public static IReadOnlyList<string> Violations(IEnumerable<TypeUsage> usages, Layers layers)
    {
        var violations = new List<string>();
        var edges = new Dictionary<string, HashSet<string>>();
        foreach (var usage in usages)
        {
            var name = usage.Type.FullName;
            if (IsCompilerInjected(usage)) continue;
            if (layers.Of(name) is not { } area)
            {
                violations.Add($"{name}: namespace has no layer; classify it in Layers.AtProtoNet");
                continue;
            }
            foreach (var referenced in usage.Types)
            {
                if (layers.Of(referenced) is not { } target || target == area) continue;
                edges.TryAdd(area.Name, []);
                edges[area.Name].Add(target.Name);
                if (target.Layer > area.Layer)
                    violations.Add($"{name} (layer {area.Layer}, {area.Name}) uses {referenced} (layer {target.Layer}, {target.Name})");
                else if (target.Layer == area.Layer && Layers.LexiconFamilies.TryGetValue(area.Name, out var allowed) && !allowed.Contains(target.Name))
                    violations.Add($"{name} ({area.Name}) uses {referenced} ({target.Name}), off the Lexicon reference graph");
            }
            if (area.Layer != 0) continue;
            foreach (var io in usage.Types.Where(t => IoTypePrefixes.Any(p => t.StartsWith(p, StringComparison.Ordinal))))
                violations.Add($"{name} (layer 0) uses {io}: layer 0 does no I/O");
            foreach (var clock in usage.Members.Where(ClockMembers.Contains))
                violations.Add($"{name} (layer 0) reads {clock}: take a TimeProvider");
        }
        violations.AddRange(Cycles(edges).Select(cycle => $"namespace cycle: {string.Join(" -> ", cycle)}"));
        return violations;
    }

    // Attributes the compiler embeds (NullableAttribute, EmbeddedAttribute, …), its helper types,
    // and the trackers coverlet instruments the assembly with when tests collect coverage.
    private static bool IsCompilerInjected(TypeUsage usage) =>
        usage.Type.FullName.StartsWith('<')
        || ((string[])["System.", "Microsoft.CodeAnalysis", "Coverlet."]).Any(p => usage.Type.Namespace.StartsWith(p, StringComparison.Ordinal));

    private static IEnumerable<string[]> Cycles(Dictionary<string, HashSet<string>> edges)
    {
        var done = new HashSet<string>();
        var path = new List<string>();
        var found = new List<string[]>();
        void Visit(string node)
        {
            var index = path.IndexOf(node);
            if (index >= 0) { found.Add([.. path[index..], node]); return; }
            if (!done.Add(node)) return;
            path.Add(node);
            foreach (var next in edges.GetValueOrDefault(node) ?? []) Visit(next);
            path.RemoveAt(path.Count - 1);
        }
        foreach (var node in edges.Keys.Order(StringComparer.Ordinal)) Visit(node);
        return found;
    }
}
