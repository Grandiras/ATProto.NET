using System.Globalization;
using System.Text;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.CodeGen;

/// <summary>
/// Maps between AT Protocol Lexicon types/NSIDs and C# types/namespaces.
/// </summary>
public static class TypeMapper
{
    private const string DefaultNamespacePrefix = "ATProtoNet.Lexicon";

    /// <summary>
    /// Converts an NSID authority (all segments except the last) to a C# namespace.
    /// "app.bsky.feed.post" → "ATProtoNet.Lexicon.App.Bsky.Feed"
    /// "com.atproto.repo.createRecord" → "ATProtoNet.Lexicon.Com.AtProto.Repo"
    /// </summary>
    public static string NsidToNamespace(string nsid, string namespacePrefix = DefaultNamespacePrefix)
    {
        var lastDot = nsid.LastIndexOf('.');
        if (lastDot < 0) return namespacePrefix;

        var authority = nsid[..lastDot];
        var segments = authority.Split('.');

        var sb = new StringBuilder(namespacePrefix);
        foreach (var segment in segments)
        {
            sb.Append('.');
            sb.Append(ToPascalCase(segment));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Gets a C# class name from an NSID and definition name.
    /// The "main" definition uses the NSID's last segment; others use the def name directly.
    /// </summary>
    /// <param name="nsid">The full NSID (e.g., "app.bsky.feed.post").</param>
    /// <param name="defName">The definition key (e.g., "main", "replyRef").</param>
    /// <param name="defType">The definition type (e.g., "record", "object").</param>
    public static string DefToClassName(string nsid, string defName, string defType)
    {
        string baseName;

        if (defName == "main")
        {
            // Use the last segment of the NSID
            var lastDot = nsid.LastIndexOf('.');
            baseName = lastDot >= 0 ? nsid[(lastDot + 1)..] : nsid;
        }
        else
        {
            baseName = defName;
        }

        var pascal = ToPascalCase(baseName);

        // Append a suffix for record types to match existing SDK convention
        if (defType == "record")
            return pascal + "Record";

        return pascal;
    }

    /// <summary>
    /// Resolves a Lexicon type reference to a C# fully-qualified type name.
    /// Handles both local (#defName) and external (nsid#defName or nsid) references.
    /// </summary>
    /// <param name="refString">The ref value, e.g., "#replyRef" or "com.atproto.repo.strongRef".</param>
    /// <param name="contextNsid">The NSID of the current document (for resolving local refs).</param>
    /// <param name="namespacePrefix">The C# namespace prefix.</param>
    public static string ResolveRef(string refString, string contextNsid, string namespacePrefix = DefaultNamespacePrefix)
    {
        if (refString.StartsWith('#'))
        {
            // Local reference — same namespace as the context NSID
            var localName = ToPascalCase(refString[1..]);
            var ns = NsidToNamespace(contextNsid, namespacePrefix);
            return $"{ns}.{localName}";
        }

        // External reference, possibly with #defName
        var hashIdx = refString.IndexOf('#');
        if (hashIdx >= 0)
        {
            var extNsid = refString[..hashIdx];
            var extDef = refString[(hashIdx + 1)..];
            var ns = NsidToNamespace(extNsid, namespacePrefix);
            return $"{ns}.{ToPascalCase(extDef)}";
        }

        // Plain NSID reference — refers to the "main" definition
        {
            var ns = NsidToNamespace(refString, namespacePrefix);
            var lastDot = refString.LastIndexOf('.');
            var name = lastDot >= 0 ? ToPascalCase(refString[(lastDot + 1)..]) : ToPascalCase(refString);
            return $"{ns}.{name}";
        }
    }

    /// <summary>
    /// Maps a Lexicon property schema to a C# type string.
    /// </summary>
    public static string GetCSharpType(LexiconSchema schema, string contextNsid, string namespacePrefix = DefaultNamespacePrefix)
    {
        return schema.Type switch
        {
            "string" => schema.Enum is { Count: > 0 } ? "string" : "string",
            "integer" => "long",
            "boolean" => "bool",
            "unknown" => "JsonElement",
            "cid-link" => "string",
            "blob" => "BlobRef",
            "bytes" => "byte[]",

            "ref" when schema.Ref is not null =>
                ResolveRef(schema.Ref, contextNsid, namespacePrefix),

            "union" => schema.Refs is { Count: > 0 }
                ? "JsonElement" // Unions are complex; default to JsonElement
                : "JsonElement",

            "array" when schema.Items is not null =>
                $"List<{GetCSharpType(schema.Items, contextNsid, namespacePrefix)}>",

            "object" => "JsonElement", // inline objects — rare, use JsonElement

            _ => "JsonElement",
        };
    }

    /// <summary>
    /// Maps a C# type name back to a Lexicon type string.
    /// </summary>
    public static string GetLexiconType(string csharpType)
    {
        // Strip nullable suffix
        if (csharpType.EndsWith('?'))
            csharpType = csharpType[..^1];

        // Handle generic List<T>
        if (csharpType.StartsWith("List<") && csharpType.EndsWith('>'))
            return "array";

        return csharpType switch
        {
            "string" or "String" => "string",
            "long" or "Int64" => "integer",
            "int" or "Int32" => "integer",
            "bool" or "Boolean" => "boolean",
            "byte[]" => "bytes",
            "JsonElement" => "unknown",
            "BlobRef" => "blob",
            "DateTime" or "DateTimeOffset" => "string", // format: datetime
            _ => "ref", // Assume it's a reference to another type
        };
    }

    /// <summary>
    /// Extracts the Lexicon string format hint from a C# type or property name.
    /// </summary>
    public static string? InferStringFormat(string propertyName, string? description)
    {
        var lower = propertyName.ToLowerInvariant();
        if (lower.Contains("createdat") || lower.Contains("indexedat") || lower.Contains("updatedat"))
            return "datetime";
        if (lower == "did" || lower.Contains("did"))
            return "did";
        if (lower == "handle")
            return "handle";
        if (lower == "uri" || lower == "aturi")
            return "at-uri";
        if (lower == "cid")
            return "cid";
        if (lower == "avatar" || lower == "banner" || lower == "thumb")
            return null; // These are blob URIs, not format-constrained strings

        if (description is not null)
        {
            var descLower = description.ToLowerInvariant();
            if (descLower.Contains("iso 8601") || descLower.Contains("datetime"))
                return "datetime";
            if (descLower.Contains("at-uri"))
                return "at-uri";
        }

        return null;
    }

    /// <summary>
    /// Converts a string to PascalCase.
    /// "getTimeline" → "GetTimeline", "reply_ref" → "ReplyRef", "feedViewPost" → "FeedViewPost"
    /// </summary>
    public static string ToPascalCase(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var sb = new StringBuilder(input.Length);
        var capitalizeNext = true;

        foreach (var ch in input)
        {
            if (ch is '_' or '-' or '.')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                sb.Append(char.ToUpper(ch, CultureInfo.InvariantCulture));
                capitalizeNext = false;
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts a PascalCase C# property name to a camelCase JSON property name.
    /// "DisplayName" → "displayName", "CreatedAt" → "createdAt"
    /// </summary>
    public static string ToCamelCase(string input)
    {
        if (string.IsNullOrEmpty(input) || char.IsLower(input[0]))
            return input;

        return char.ToLower(input[0], CultureInfo.InvariantCulture) + input[1..];
    }

    /// <summary>
    /// Gets the relative file path for a generated C# file from an NSID.
    /// "app.bsky.feed.post" → "App/Bsky/Feed/PostRecord.g.cs"
    /// </summary>
    public static string NsidToFilePath(string nsid, string defName, string defType)
    {
        var lastDot = nsid.LastIndexOf('.');
        var authority = lastDot >= 0 ? nsid[..lastDot] : nsid;
        var segments = authority.Split('.');
        var className = DefToClassName(nsid, defName, defType);

        var path = string.Join("/", segments.Select(ToPascalCase));
        return $"{path}/{className}.g.cs";
    }

    /// <summary>
    /// Parses a <c>$type</c> value into an NSID and optional definition name.
    /// "app.bsky.feed.post" → ("app.bsky.feed.post", "main")
    /// "com.atproto.label.defs#selfLabels" → ("com.atproto.label.defs", "selfLabels")
    /// </summary>
    public static (string Nsid, string DefName) ParseTypeValue(string typeValue)
    {
        var hashIdx = typeValue.IndexOf('#');
        if (hashIdx >= 0)
            return (typeValue[..hashIdx], typeValue[(hashIdx + 1)..]);

        return (typeValue, "main");
    }
}
