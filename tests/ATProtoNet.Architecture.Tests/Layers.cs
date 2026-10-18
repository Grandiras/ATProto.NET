namespace ATProtoNet.Architecture.Tests;

/// <summary>A namespace area of the base package and the layer it sits in (design/02-architecture.md).</summary>
public sealed record Area(string Name, int Layer);

/// <summary>Classifies the base package's namespaces into areas and layers.</summary>
public sealed class Layers(IReadOnlyList<(string Namespace, Area Area)> map, Func<string, Area?>? rootType = null)
{
    /// <summary>The layers of <c>ATProtoNet</c>. A new namespace must be added here before it builds green.</summary>
    public static Layers AtProtoNet { get; } = new(
    [
        ("ATProtoNet.Data", new("Data", 0)),
        ("ATProtoNet.Crypto", new("Crypto", 0)),
        ("ATProtoNet.Repo", new("Repo", 0)),
        ("ATProtoNet.Spaces.Access", new("Spaces.Access", 2)),
        ("ATProtoNet.Spaces.Sync", new("Spaces.Sync", 3)),
        ("ATProtoNet.Spaces", new("Spaces", 0)),
        ("ATProtoNet.Lexicon", new("Lexicon", 0)),
        ("ATProtoNet.Net", new("Net", 1)),
        ("ATProtoNet.Identity", new("Identity", 1)),
        ("ATProtoNet.Xrpc", new("Xrpc", 2)),
        ("ATProtoNet.Auth", new("Auth", 2)),
        ("ATProtoNet.Labels", new("Labels", 2)),
        ("ATProtoNet.Records", new("Records", 2)),
        ("ATProtoNet.Admin", new("Admin", 2)),
        ("ATProtoNet.Lexicons.Com.AtProto", new("Lexicons.Com.AtProto", 2)),
        ("ATProtoNet.Streaming", new("Streaming", 3)),
        ("ATProtoNet.Bluesky", new("Bluesky", 3)),
        ("ATProtoNet.Lexicons.App.Bsky", new("Lexicons.App.Bsky", 3)),
        ("ATProtoNet.Lexicons.Chat.Bsky", new("Lexicons.Chat.Bsky", 3)),
        ("ATProtoNet.Lexicons.Tools.Ozone", new("Lexicons.Tools.Ozone", 3)),
        ("ATProtoNet.Lexicons.Site.Standard", new("Lexicons.Site.Standard", 3)),
        ("ATProtoNet.Hosting", new("Hosting", 4)),
        // Registration extension methods live in the framework's namespaces, per convention.
        ("Microsoft.Extensions.DependencyInjection", new("Hosting", 4)),
        ("Microsoft.Extensions.Hosting", new("Hosting", 4)),
    ],
    // The root namespace mixes layer-0 vocabulary with the client (layer 2).
    name => name.StartsWith("AtProtoClient", StringComparison.Ordinal) ? new("Client", 2) : new("Root", 0));

    /// <summary>Lexicon families may reference each other only along the Lexicon reference graph.</summary>
    public static IReadOnlyDictionary<string, string[]> LexiconFamilies { get; } = new Dictionary<string, string[]>
    {
        ["Lexicons.Com.AtProto"] = [],
        ["Lexicons.App.Bsky"] = ["Lexicons.Com.AtProto"],
        ["Lexicons.Chat.Bsky"] = ["Lexicons.Com.AtProto", "Lexicons.App.Bsky"],
        ["Lexicons.Tools.Ozone"] = ["Lexicons.Com.AtProto", "Lexicons.App.Bsky", "Lexicons.Chat.Bsky"],
        ["Lexicons.Site.Standard"] = ["Lexicons.Com.AtProto"],
    };

    /// <summary>The area of a type by its full name, or <see langword="null"/> for a type outside the package's namespaces.</summary>
    public Area? Of(string fullName)
    {
        var outer = fullName.Split('/')[0];
        var dot = outer.LastIndexOf('.');
        var ns = dot < 0 ? "" : outer[..dot];
        foreach (var (prefix, area) in map)
            if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal)) return area;
        return rootType is not null && ns == RootNamespace ? rootType(outer[(dot + 1)..]) : null;
    }

    /// <summary>The root namespace the <c>rootType</c> classifier applies to.</summary>
    public string RootNamespace { get; init; } = "ATProtoNet";
}
