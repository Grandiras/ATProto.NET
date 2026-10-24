using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace ATProtoNet.Lexicon.CodeGen;

/// <summary>A generation unit: the Lexicons whose code shares one <c>{Name}Lexicons</c> class and serialization context (LEX-30).</summary>
/// <param name="Name">The unit name, such as <c>AtProto</c>; it also names the unit's output folder.</param>
/// <param name="Include">NSID glob patterns of the unit's Lexicons.</param>
public sealed record LexgenUnit(string Name, IReadOnlyList<string> Include);

/// <summary>Generation options, from <c>lexgen.json</c> (LEX-31).</summary>
public sealed record LexgenConfig
{
    /// <summary>The namespace root (D-05).</summary>
    public string RootNamespace { get; init; } = "ATProtoNet.Lexicons";

    /// <summary>The generation units; one unit named <c>Lexicons</c> covering everything when empty.</summary>
    public IReadOnlyList<LexgenUnit> Units { get; init; } = [];

    /// <summary>NSID globs to generate; everything when empty.</summary>
    public IReadOnlyList<string> Include { get; init; } = [];

    /// <summary>NSID globs never to generate.</summary>
    public IReadOnlyList<string> Exclude { get; init; } = ["internal.**"];

    /// <summary>NSID authority prefix to client property name, or <c>@root</c> for members on <c>AtProtoClient</c> itself.</summary>
    public IReadOnlyDictionary<string, string> Accessors { get; init; } = new Dictionary<string, string>();

    /// <summary><c>nsid#def</c> or <c>nsid#def.property</c> to C# name.</summary>
    public IReadOnlyDictionary<string, string> Renames { get; init; } = new Dictionary<string, string>();

    /// <summary><c>nsid#def.property</c> to a C# type for a plain string field; only <c>decimal</c> (LEX-92).</summary>
    public IReadOnlyDictionary<string, string> FieldTypes { get; init; } = new Dictionary<string, string>();

    /// <summary>NSID glob to the default service proxy, such as <c>did:web:api.bsky.chat#bsky_chat</c>.</summary>
    public IReadOnlyDictionary<string, string> Proxies { get; init; } = new Dictionary<string, string>();

    /// <summary>NSID globs whose proxy default does not apply.</summary>
    public IReadOnlyList<string> ProxyExclude { get; init; } = [];

    /// <summary>NSID glob to the diagnostic ID of an <c>[Experimental]</c> marker.</summary>
    public IReadOnlyDictionary<string, string> Experimental { get; init; } = new Dictionary<string, string>();

    /// <summary><c>nsid#def.property</c> globs whose <c>at-uri</c> fields map to <c>AtAddress</c> (LEX-88).</summary>
    public IReadOnlyList<string> AddressFields { get; init; } = [];

    /// <summary><c>nsid#def</c> to the open unions in referenced assemblies it extends (LEX-86).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> UnionExtensions { get; init; } = new Dictionary<string, IReadOnlyList<string>>();

    /// <summary>Whether generated types are <c>internal</c> instead of <c>public</c>.</summary>
    public bool Internal { get; init; }

    /// <summary>Parses <c>lexgen.json</c>. Keys starting with <c>//</c> are comments.</summary>
    /// <param name="json">The file's text.</param>
    /// <returns>The options.</returns>
    /// <exception cref="FormatException">The file is not valid JSON, has an unknown key or a value of the wrong kind.</exception>
    public static LexgenConfig Parse(string json)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            root = document.RootElement.Clone();
        }
        catch (JsonException e)
        {
            throw new FormatException("lexgen.json is not valid JSON: " + e.Message, e);
        }
        if (root.ValueKind != JsonValueKind.Object) throw new FormatException("lexgen.json is a JSON object.");
        var config = new LexgenConfig();
        foreach (var property in root.EnumerateObject())
            config = property.Name switch
            {
                _ when property.Name.StartsWith("//", StringComparison.Ordinal) || property.Name == "$schema" => config,
                "rootNamespace" => config with { RootNamespace = Text(property) },
                "units" => config with { Units = [.. Map(property).Select(u => new LexgenUnit(u.Key, List(u.Value, "units." + u.Key)))] },
                "include" => config with { Include = List(property.Value, property.Name) },
                "exclude" => config with { Exclude = List(property.Value, property.Name) },
                "accessors" => config with { Accessors = Strings(property) },
                "renames" => config with { Renames = Strings(property) },
                "fieldTypes" => config with { FieldTypes = Strings(property) },
                "proxies" => config with { Proxies = Strings(property) },
                "proxyExclude" => config with { ProxyExclude = List(property.Value, property.Name) },
                "experimental" => config with { Experimental = Strings(property) },
                "addressFields" => config with { AddressFields = List(property.Value, property.Name) },
                "unionExtensions" => config with { UnionExtensions = Map(property).ToDictionary(e => e.Key, e => List(e.Value, "unionExtensions." + e.Key)) },
                "visibility" => config with
                {
                    Internal = Text(property) switch
                    {
                        "public" => false,
                        "internal" => true,
                        var other => throw new FormatException($"\"visibility\" is \"public\" or \"internal\", not \"{other}\"."),
                    },
                },
                _ => throw new FormatException($"lexgen.json has an unknown option \"{property.Name}\"."),
            };
        return config;
    }

    private static string Text(JsonProperty property) =>
        property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : throw new FormatException($"\"{property.Name}\" is a string.");

    private static IReadOnlyList<string> List(JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Array && value.EnumerateArray().All(v => v.ValueKind == JsonValueKind.String)
            ? [.. value.EnumerateArray().Select(v => v.GetString()!)]
            : throw new FormatException($"\"{name}\" is an array of strings.");

    private static IEnumerable<KeyValuePair<string, JsonElement>> Map(JsonProperty property) =>
        property.Value.ValueKind == JsonValueKind.Object
            ? property.Value.EnumerateObject().Where(p => !p.Name.StartsWith("//", StringComparison.Ordinal)).Select(p => new KeyValuePair<string, JsonElement>(p.Name, p.Value))
            : throw new FormatException($"\"{property.Name}\" is an object.");

    // String values; an object value {"name": …, "reason": …} carries a reason for review (LEX-91), and only its name counts.
    private static IReadOnlyDictionary<string, string> Strings(JsonProperty property) =>
        Map(property).ToDictionary(e => e.Key, e => e.Value.ValueKind switch
        {
            JsonValueKind.String => e.Value.GetString()!,
            JsonValueKind.Object when e.Value.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String => name.GetString()!,
            _ => throw new FormatException($"\"{property.Name}.{e.Key}\" is a string."),
        }, StringComparer.Ordinal);
}
