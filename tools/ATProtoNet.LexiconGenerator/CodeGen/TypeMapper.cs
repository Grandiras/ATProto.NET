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
            sb.Append(ToNamespaceSegment(segment));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Casing for one NSID segment used as a namespace or folder name. Known acronyms keep
    /// the SDK's spelling so generated code sits alongside <c>ATProtoNet.Lexicon.Com.AtProto.*</c>.
    /// </summary>
    public static string ToNamespaceSegment(string segment)
        => s_segmentCasing.TryGetValue(segment, out var cased) ? cased : ToPascalCase(segment);

    private static readonly Dictionary<string, string> s_segmentCasing = new(StringComparer.OrdinalIgnoreCase)
    {
        ["atproto"] = "AtProto",
    };

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
    /// Normalizes a Lexicon ref to its canonical <c>nsid#defName</c> form.
    /// "#image" (in com.example.foo) → "com.example.foo#image";
    /// "com.atproto.repo.strongRef" → "com.atproto.repo.strongRef#main".
    /// </summary>
    public static string NormalizeRef(string refString, string contextNsid)
    {
        if (refString.StartsWith('#'))
            return $"{contextNsid}#{refString[1..]}";

        return refString.Contains('#') ? refString : $"{refString}#main";
    }

    /// <summary>Splits the NSID off a normalized ref.</summary>
    public static (string Nsid, string DefName) SplitRef(string normalizedRef)
    {
        var hashIdx = normalizedRef.IndexOf('#');
        return hashIdx >= 0
            ? (normalizedRef[..hashIdx], normalizedRef[(hashIdx + 1)..])
            : (normalizedRef, "main");
    }

    /// <summary>
    /// The <c>$type</c> discriminator value for a definition — the bare NSID for
    /// <c>main</c>, otherwise <c>nsid#defName</c>.
    /// </summary>
    public static string TypeValue(string nsid, string defName)
        => defName == "main" ? nsid : $"{nsid}#{defName}";

    /// <summary>
    /// Maps a Lexicon property schema to a C# type string.
    /// </summary>
    /// <remarks>
    /// This is the context-free mapping used for simple scalars. The emitter uses its own
    /// resolution for refs, unions, and inline objects because those need cross-document
    /// knowledge (see <see cref="EmitPlan"/>).
    /// </remarks>
    public static string GetCSharpType(LexiconSchema schema, string contextNsid, string namespacePrefix = DefaultNamespacePrefix)
    {
        return schema.Type switch
        {
            "string" => "string",
            "integer" => "long",
            // Not part of the Lexicon spec, but real-world third-party schemas use it.
            "number" => "double",
            "boolean" => "bool",
            "unknown" => "JsonElement",
            "cid-link" => "string",
            "blob" => "BlobRef",
            "bytes" => "byte[]",

            "ref" when schema.Ref is not null =>
                ResolveRef(schema.Ref, contextNsid, namespacePrefix),

            "array" when schema.Items is not null =>
                $"List<{GetCSharpType(schema.Items, contextNsid, namespacePrefix)}>",

            // Unions and inline objects need document context to type properly.
            _ => "JsonElement",
        };
    }

    /// <summary>
    /// Splits an identifier into words on camel-case humps and <c>_</c>/<c>-</c>/<c>.</c> separators.
    /// "cookingMethodBake" → ["cooking", "Method", "Bake"]
    /// </summary>
    public static List<string> SplitWords(string input)
    {
        var words = new List<string>();
        if (string.IsNullOrEmpty(input))
            return words;

        var current = new StringBuilder();

        foreach (var ch in input)
        {
            if (ch is '_' or '-' or '.' or ' ')
            {
                if (current.Length > 0) { words.Add(current.ToString()); current.Clear(); }
                continue;
            }

            if (char.IsUpper(ch) && current.Length > 0 && !char.IsUpper(current[^1]))
            {
                words.Add(current.ToString());
                current.Clear();
            }

            current.Append(ch);
        }

        if (current.Length > 0)
            words.Add(current.ToString());

        return words;
    }

    /// <summary>
    /// Best-effort English singularization, used to name the element type generated for
    /// an array of inline objects ("ingredients" → "Ingredient").
    /// </summary>
    public static string Singularize(string word)
    {
        if (word.Length < 4)
            return word;

        if (word.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            return word[..^3] + "y";
        if (word.EndsWith("sses", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("shes", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("ches", StringComparison.OrdinalIgnoreCase)
            || word.EndsWith("xes", StringComparison.OrdinalIgnoreCase))
            return word[..^2];
        if (word.EndsWith("ss", StringComparison.OrdinalIgnoreCase) || word.EndsWith("us", StringComparison.OrdinalIgnoreCase))
            return word;
        if (word.EndsWith('s') || word.EndsWith('S'))
            return word[..^1];

        return word;
    }

    /// <summary>
    /// Turns an arbitrary Lexicon name into a legal C# identifier: strips characters that
    /// are not valid, prefixes a leading digit with <c>_</c>, and escapes C# keywords.
    /// </summary>
    public static string ToIdentifier(string name, string fallback = "Value")
    {
        if (string.IsNullOrEmpty(name))
            return fallback;

        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_')
                sb.Append(ch);
        }

        if (sb.Length == 0)
            return fallback;

        if (char.IsDigit(sb[0]))
            sb.Insert(0, '_');

        var identifier = sb.ToString();
        return s_keywords.Contains(identifier) ? "@" + identifier : identifier;
    }

    private static readonly HashSet<string> s_keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
        "short", "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
        "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using",
        "virtual", "void", "volatile", "while",
    };

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

        var path = string.Join("/", segments.Select(ToNamespaceSegment));
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
