using System;
using System.Collections.Generic;
using System.Text.Json;

// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26).
namespace ATProtoNet.Lexicon;

// The Lexicon JSON reader: one pass over the document, a diagnostic per violation (LEX-3, LEX-4).
internal sealed partial class LexiconParser
{
    private readonly List<LexiconDiagnostic> _diagnostics = [];
    private string? _id;

    public IReadOnlyList<LexiconDiagnostic> Diagnostics => _diagnostics;

    public bool HasErrors { get; private set; }

    public void Error(string rule, string message, string pointer)
    {
        HasErrors = true;
        _diagnostics.Add(new(rule, LexiconSeverity.Error, message, _id, pointer));
    }

    private void Warning(string rule, string message, string pointer) => _diagnostics.Add(new(rule, LexiconSeverity.Warning, message, _id, pointer));

    public (Nsid Id, int? Revision, string? Description, IReadOnlyDictionary<string, LexUserType> Defs)? Document(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            Error(LexiconRules.DocumentShape, "A Lexicon document is a JSON object.", "");
            return null;
        }
        if (!root.TryGetProperty("lexicon", out var version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1)
            Error(LexiconRules.DocumentShape, "\"lexicon\" must be the number 1.", "/lexicon");
        Nsid? id = null;
        if (!root.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String)
            Error(LexiconRules.DocumentShape, "\"id\" must be a string.", "/id");
        else if (!Nsid.TryParse(idElement.GetString(), out id))
            Error(LexiconRules.DocumentShape, $"\"id\" is not a valid NSID: \"{idElement.GetString()}\".", "/id");
        _id = id?.ToString();
        var revision = OptionalInt(root, "revision", "");
        var description = OptionalString(root, "description", "");
        var defs = new Dictionary<string, LexUserType>(StringComparer.Ordinal);
        if (!root.TryGetProperty("defs", out var defsElement) || defsElement.ValueKind != JsonValueKind.Object)
            Error(LexiconRules.DocumentShape, "\"defs\" must be an object.", "/defs");
        else
            foreach (var def in defsElement.EnumerateObject())
            {
                var pointer = "/defs/" + Escape(def.Name);
                if (defs.Count == LexiconLimits.MaxDefinitionsPerDocument)
                {
                    Error(LexiconRules.DocumentShape, $"A document has at most {LexiconLimits.MaxDefinitionsPerDocument} definitions.", pointer);
                    break;
                }
                var type = TypeOf(def.Value, pointer);
                if (type is null) continue;
                if (IsPrimary(type) && def.Name != "main")
                    Error(LexiconRules.PrimaryType, $"A {type} must be the \"main\" definition.", pointer);
                if (type is "ref" or "unknown" or "params")
                    Error(LexiconRules.ForbiddenDefinition, $"A {type} cannot be a named definition.", pointer);
                if (type == "union") Warning(LexiconRules.NamedUnion, "The Lexicon spec does not allow a named union; inline it where it is used.", pointer);
                if (Schema(def.Value, pointer, 0, SchemaPosition.Definition) is LexUserType schema) defs[def.Name] = schema;
            }
        if (defsElement.ValueKind == JsonValueKind.Object && defs.Count == 0 && !HasErrors)
            Error(LexiconRules.DocumentShape, "A Lexicon document has at least one definition.", "/defs");
        return id is null ? null : (id, revision, description, defs);
    }

    private enum SchemaPosition { Definition, Field, Parameter, Body }

    private string? TypeOf(JsonElement node, string pointer)
    {
        if (node.ValueKind != JsonValueKind.Object)
        {
            Error(LexiconRules.InvalidSchema, node.ValueKind == JsonValueKind.Null ? "A schema cannot be null." : "A schema is a JSON object.", pointer);
            return null;
        }
        if (node.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String) return type.GetString()!;
        Error(LexiconRules.InvalidSchema, "A schema has a string \"type\".", pointer + "/type");
        return null;
    }

    private LexSchema? Schema(JsonElement node, string pointer, int depth, SchemaPosition position)
    {
        var type = TypeOf(node, pointer);
        if (type is null) return null;
        if (depth > LexiconLimits.MaxSchemaNestingDepth)
        {
            Error(LexiconRules.InvalidSchema, $"Schemas nest at most {LexiconLimits.MaxSchemaNestingDepth} levels deep.", pointer);
            return null;
        }
        if (position != SchemaPosition.Definition && (IsPrimary(type) || type is "token" or "params"))
        {
            Error(LexiconRules.InvalidSchema, $"A {type} can only be a definition.", pointer);
            return null;
        }
        if (position == SchemaPosition.Body && type is not ("object" or "ref" or "union"))
        {
            Error(LexiconRules.InvalidSchema, $"A body schema is an object, ref or union, not a {type}.", pointer);
            return null;
        }
        var description = OptionalString(node, "description", pointer);
        LexUserType? schema = type switch
        {
            "record" => Record(node, pointer, depth),
            "query" => new LexQuery { Parameters = Params(node, pointer, depth), Output = Body(node, "output", pointer, depth), Errors = Errors(node, pointer) },
            "procedure" => new LexProcedure
            {
                Parameters = Params(node, pointer, depth),
                Input = Body(node, "input", pointer, depth),
                Output = Body(node, "output", pointer, depth),
                Errors = Errors(node, pointer),
            },
            "subscription" => new LexSubscription { Parameters = Params(node, pointer, depth), Message = Body(node, "message", pointer, depth), Errors = Errors(node, pointer) },
            "permission-set" => PermissionSet(node, pointer),
            "space" => new LexSpace
            {
                Key = OptionalString(node, "key", pointer),
                Name = OptionalString(node, "name", pointer),
                NameLang = Localized(node, "name:lang", pointer),
                Collections = StringList(node, "collections", pointer) ?? [],
            },
            "token" => new LexToken(),
            "object" => Object(node, pointer, depth),
            "params" => ParamsBody(node, pointer, depth),
            "string" => new LexString
            {
                Format = OptionalString(node, "format", pointer),
                MinLength = OptionalLong(node, "minLength", pointer),
                MaxLength = OptionalLong(node, "maxLength", pointer),
                MinGraphemes = OptionalLong(node, "minGraphemes", pointer),
                MaxGraphemes = OptionalLong(node, "maxGraphemes", pointer),
                Enum = StringList(node, "enum", pointer),
                KnownValues = StringList(node, "knownValues", pointer),
                Const = OptionalString(node, "const", pointer),
                Default = OptionalString(node, "default", pointer),
            },
            "integer" => new LexInteger
            {
                Minimum = OptionalLong(node, "minimum", pointer),
                Maximum = OptionalLong(node, "maximum", pointer),
                Enum = LongList(node, "enum", pointer),
                Const = OptionalLong(node, "const", pointer),
                Default = OptionalLong(node, "default", pointer),
            },
            "boolean" => new LexBoolean { Const = OptionalBool(node, "const", pointer), Default = OptionalBool(node, "default", pointer) },
            "bytes" => new LexBytes { MinLength = OptionalLong(node, "minLength", pointer), MaxLength = OptionalLong(node, "maxLength", pointer) },
            "cid-link" => new LexCidLink(),
            "blob" => new LexBlob { Accept = StringList(node, "accept", pointer), MaxSize = OptionalLong(node, "maxSize", pointer) },
            "array" => Array(node, pointer, depth, position),
            "unknown" => new LexUnknown(),
            "ref" => RequiredString(node, "ref", pointer) is { } target ? new LexRef { Ref = target } : null,
            "union" => StringList(node, "refs", pointer) is { } refs
                ? new LexUnion { Refs = refs, Closed = OptionalBool(node, "closed", pointer) ?? false }
                : Missing<LexUnion>("refs", pointer),
            _ => null,
        };
        if (type == "null")
        {
            Error(LexiconRules.InvalidSchema, "The \"null\" type was removed from the Lexicon language.", pointer + "/type");
            return null;
        }
        if (schema is null && !IsKnownType(type))
        {
            Warning(LexiconRules.UnknownType, $"The type \"{type}\" is not known to this version; it is kept but not used.", pointer + "/type");
            schema = new LexUnknownType { TypeName = type };
        }
        if (schema is not null && position == SchemaPosition.Parameter && type is not ("boolean" or "integer" or "string")
            && !(schema is LexArray { Items: var items } && items is LexBoolean or LexInteger or LexString))
            Error(LexiconRules.ParameterType, $"A parameter is a boolean, integer, string or an array of those, not a {type}.", pointer);
        return schema is null ? null : schema with { Description = description, Raw = node };
    }

    internal static bool IsPrimary(string type) => type is "record" or "query" or "procedure" or "subscription" or "permission-set" or "space";

    private static bool IsKnownType(string type) =>
        IsPrimary(type) || type is "token" or "object" or "params" or "string" or "integer" or "boolean" or "bytes" or "cid-link"
            or "blob" or "array" or "unknown" or "ref" or "union";

    private T? Missing<T>(string name, string pointer) where T : class
    {
        Error(LexiconRules.InvalidSchema, $"\"{name}\" is required.", pointer + "/" + Escape(name));
        return null;
    }

    private LexRecord? Record(JsonElement node, string pointer, int depth)
    {
        var keyText = RequiredString(node, "key", pointer);
        RecordKeyType? key = null;
        if (keyText is not null && !RecordKeyType.TryParse(keyText, out key))
            Error(LexiconRules.InvalidSchema, $"\"{keyText}\" is not a record key type (tid, nsid, any or literal:<value>).", pointer + "/key");
        if (!node.TryGetProperty("record", out var recordNode)) return Missing<LexRecord>("record", pointer);
        var type = TypeOf(recordNode, pointer + "/record");
        if (type is null) return null;
        if (type != "object")
        {
            Error(LexiconRules.InvalidSchema, "A record's \"record\" is an object schema.", pointer + "/record/type");
            return null;
        }
        return Object(recordNode, pointer + "/record", depth + 1) is { } record && key is not null
            ? new LexRecord { Key = key, Record = record with { Description = OptionalString(recordNode, "description", pointer + "/record"), Raw = recordNode } }
            : null;
    }

    private LexObject? Object(JsonElement node, string pointer, int depth)
    {
        var properties = Properties(node, pointer, depth, SchemaPosition.Field);
        var required = StringList(node, "required", pointer) ?? [];
        var nullable = StringList(node, "nullable", pointer) ?? [];
        return properties is null ? null : new LexObject { Properties = properties, Required = required, Nullable = nullable };
    }

    private LexParams? ParamsBody(JsonElement node, string pointer, int depth)
    {
        var properties = Properties(node, pointer, depth, SchemaPosition.Parameter);
        var required = StringList(node, "required", pointer) ?? [];
        return properties is null ? null : new LexParams { Properties = properties, Required = required };
    }

    private Dictionary<string, LexSchema>? Properties(JsonElement node, string pointer, int depth, SchemaPosition position)
    {
        var properties = new Dictionary<string, LexSchema>(StringComparer.Ordinal);
        if (!node.TryGetProperty("properties", out var element)) return properties;
        if (element.ValueKind != JsonValueKind.Object)
        {
            Error(LexiconRules.InvalidSchema, "\"properties\" is an object.", pointer + "/properties");
            return null;
        }
        foreach (var property in element.EnumerateObject())
            if (Schema(property.Value, pointer + "/properties/" + Escape(property.Name), depth + 1, position) is { } schema)
                properties[property.Name] = schema;
        return properties;
    }

    private LexArray? Array(JsonElement node, string pointer, int depth, SchemaPosition position)
    {
        var minLength = OptionalLong(node, "minLength", pointer);
        var maxLength = OptionalLong(node, "maxLength", pointer);
        if (!node.TryGetProperty("items", out var itemsNode)) return Missing<LexArray>("items", pointer);
        var items = Schema(itemsNode, pointer + "/items", depth + 1, position == SchemaPosition.Parameter ? SchemaPosition.Field : position);
        return items is null ? null : new LexArray { Items = items, MinLength = minLength, MaxLength = maxLength };
    }

    private LexParams? Params(JsonElement node, string pointer, int depth)
    {
        if (!node.TryGetProperty("parameters", out var element)) return null;
        var type = TypeOf(element, pointer + "/parameters");
        if (type is null) return null;
        if (type != "params")
        {
            Error(LexiconRules.InvalidSchema, "\"parameters\" is a params schema.", pointer + "/parameters/type");
            return null;
        }
        return ParamsBody(element, pointer + "/parameters", depth + 1) is { } parameters
            ? parameters with { Description = OptionalString(element, "description", pointer + "/parameters"), Raw = element }
            : null;
    }

    private LexBody? Body(JsonElement node, string name, string pointer, int depth)
    {
        if (!node.TryGetProperty(name, out var element)) return null;
        pointer += "/" + name;
        if (element.ValueKind != JsonValueKind.Object)
        {
            Error(LexiconRules.InvalidSchema, $"\"{name}\" is an object.", pointer);
            return null;
        }
        var encoding = name == "message" ? OptionalString(element, "encoding", pointer) ?? "" : RequiredString(element, "encoding", pointer);
        LexSchema? schema = null;
        if (element.TryGetProperty("schema", out var schemaNode)) schema = Schema(schemaNode, pointer + "/schema", depth + 1, SchemaPosition.Body);
        return encoding is null ? null : new LexBody { Encoding = encoding, Description = OptionalString(element, "description", pointer), Schema = schema };
    }

    private IReadOnlyList<LexError> Errors(JsonElement node, string pointer)
    {
        if (!node.TryGetProperty("errors", out var element)) return [];
        if (element.ValueKind != JsonValueKind.Array)
        {
            Error(LexiconRules.InvalidSchema, "\"errors\" is an array.", pointer + "/errors");
            return [];
        }
        var errors = new List<LexError>();
        var index = 0;
        foreach (var error in element.EnumerateArray())
        {
            var errorPointer = pointer + "/errors/" + index++;
            if (error.ValueKind != JsonValueKind.Object)
                Error(LexiconRules.InvalidSchema, "An error is an object.", errorPointer);
            else if (RequiredString(error, "name", errorPointer) is { } name)
                errors.Add(new(name, OptionalString(error, "description", errorPointer)));
        }
        return errors;
    }

    private LexPermissionSet PermissionSet(JsonElement node, string pointer)
    {
        var permissions = new List<LexPermission>();
        if (node.TryGetProperty("permissions", out var element))
        {
            if (element.ValueKind != JsonValueKind.Array) Error(LexiconRules.InvalidSchema, "\"permissions\" is an array.", pointer + "/permissions");
            else
                foreach (var permission in element.EnumerateArray())
                    permissions.Add(new()
                    {
                        Resource = permission.ValueKind == JsonValueKind.Object && permission.TryGetProperty("resource", out var resource)
                            && resource.ValueKind == JsonValueKind.String ? resource.GetString() : null,
                        Raw = permission,
                    });
        }
        return new()
        {
            Title = OptionalString(node, "title", pointer),
            TitleLang = Localized(node, "title:lang", pointer),
            Detail = OptionalString(node, "detail", pointer),
            DetailLang = Localized(node, "detail:lang", pointer),
            Permissions = permissions,
        };
    }
}
