using System.Globalization;
using System.Text;
using ATProtoNet.Data;

namespace ATProtoNet.Lexicon.Validation;

/// <summary>Validates data-model values against the definitions of a <see cref="LexiconCatalog"/> (LEX-18–LEX-20).</summary>
/// <remarks>
/// Open semantics hold (LEX-19): unknown object properties, <c>knownValues</c>, unknown string formats and unlisted
/// members of open unions pass, as does data under a definition outside the catalog or of a type this version
/// does not know. Validation stops <see cref="LexiconLimits.MaxValidationDepth"/> levels deep and after
/// <see cref="LexiconLimits.MaxValidationErrors"/> violations. See <see href="https://atproto.com/specs/lexicon"/>.
/// </remarks>
/// <param name="catalog">The Lexicons; every reference resolves through it.</param>
public sealed class LexiconValidator(LexiconCatalog catalog)
{
    private readonly LexiconCatalog _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));

    /// <summary>Validates a value against a definition; for a record definition, also its <c>$type</c>.</summary>
    /// <param name="definition">The definition, such as <c>app.bsky.feed.post</c> or <c>app.bsky.embed.images#image</c>.</param>
    /// <param name="value">The value.</param>
    /// <returns>Every violation, with its data path.</returns>
    /// <exception cref="ArgumentException">The catalog has no schema for <paramref name="definition"/>.</exception>
    public LexiconValidationResult Validate(LexRefTarget definition, DataModelValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var resolved = Resolve(definition, nameof(definition));
        var run = new Run(_catalog);
        if (resolved.Definition is LexRecord) run.CheckRecordType(resolved.Target.Nsid, value);
        run.Check(resolved.Definition!, resolved.Target.Nsid, value);
        return run.Result();
    }

    /// <summary>Validates a record: its value, its <c>$type</c> against <paramref name="collection"/>, and its key against the record's key type.</summary>
    /// <param name="collection">The collection: the NSID of a record Lexicon.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="value">The record.</param>
    /// <returns>Every violation, with its data path; <c>rkey</c> for the record key.</returns>
    /// <exception cref="ArgumentException">The catalog has no record Lexicon <paramref name="collection"/>.</exception>
    public LexiconValidationResult ValidateRecord(Nsid collection, RecordKey rkey, DataModelValue value)
    {
        ArgumentNullException.ThrowIfNull(collection);
        ArgumentNullException.ThrowIfNull(rkey);
        ArgumentNullException.ThrowIfNull(value);
        if (Resolve(new(collection, "main"), nameof(collection)).Definition is not LexRecord record)
            throw new ArgumentException($"{collection} is not a record Lexicon.", nameof(collection));
        var run = new Run(_catalog);
        if (!Allows(record.Key, rkey)) run.Add("rkey", $"The record key does not match the key type {record.Key}.");
        run.CheckRecordType(collection, value);
        run.Check(record, collection, value);
        return run.Result();
    }

    private ResolvedDefinition Resolve(LexRefTarget target, string parameter) =>
        target.Nsid is not null && _catalog.TryResolve(target, out var resolved) && resolved.Definition is not null
            ? resolved
            : throw new ArgumentException($"The catalog has no schema for {target}.", parameter);

    // LEX-20.
    private static bool Allows(RecordKeyType type, RecordKey key) => type.Text switch
    {
        "tid" => Tid.TryParse(key.Value, out _),
        "nsid" => Nsid.TryParse(key.Value, out _),
        _ => type.Literal is null || key.Value == type.Literal,
    };

    // One validation: the violations so far and the path to the value being checked.
    private sealed class Run(LexiconCatalog catalog)
    {
        private readonly List<LexiconViolation> _violations = [];
        private readonly List<(string? Name, int Index)> _path = [];

        private bool Full => _violations.Count >= LexiconLimits.MaxValidationErrors;

        public LexiconValidationResult Result() => new(_violations);

        public void Add(string path, string message)
        {
            if (!Full) _violations.Add(new(path, message));
        }

        public void CheckRecordType(Nsid collection, DataModelValue value)
        {
            if (value is DataModelMap map && !(TypeOf(map) is { } type && ParseType(type) == new LexRefTarget(collection, "main")))
                Add(PathTo("$type"), $"A record of {collection} has the $type \"{collection}\".");
        }

        public void Check(LexSchema schema, Nsid context, DataModelValue value)
        {
            if (Full) return;
            switch (schema)
            {
                case LexRecord record: Check(record.Record, context, value); break;
                case LexObject obj: CheckObject(obj.Properties, obj.Required, obj.Nullable, context, value); break;
                case LexParams parameters: CheckObject(parameters.Properties, parameters.Required, [], context, value); break;
                case LexRef reference: CheckRef(reference.Ref, context, value); break;
                case LexUnion union: CheckUnion(union, context, value); break;
                case LexArray array: CheckArray(array, context, value); break;
                case LexString text: CheckString(text, value); break;
                case LexInteger integer: CheckInteger(integer, value); break;
                case LexBoolean boolean:
                    if (Expect<DataModelBoolean>(value, "a boolean") is { } flag && boolean.Const is { } constant && flag.Value != constant) Error($"Must be {(constant ? "true" : "false")}.");
                    break;
                case LexBytes bytes:
                    if (Expect<DataModelBytes>(value, "bytes") is { } data) Length(data.Value.Length, bytes.MinLength, bytes.MaxLength, "bytes");
                    break;
                case LexCidLink: Expect<DataModelLink>(value, "a link"); break;
                case LexBlob blob: CheckBlob(blob, value); break;
                case LexUnknown:
                    if (value is not DataModelMap map || IsBlob(map)) Error($"Expected an object, got {KindOf(value)}.");
                    break;
                case LexUnknownType: break; // a newer type this version cannot check (LEX-4)
                default: Error($"A {schema.GetType().Name} does not describe data."); break;
            }
        }

        private void CheckObject(IReadOnlyDictionary<string, LexSchema> properties, IReadOnlyList<string> required, IReadOnlyList<string> nullable, Nsid context, DataModelValue value)
        {
            if (Expect<DataModelMap>(value, "an object") is not { } map || TooDeep()) return;
            foreach (var name in required)
                if (!map.ContainsKey(name)) Add(PathTo(name), "Required, but missing.");
            foreach (var (name, schema) in properties)
            {
                if (Full) return;
                if (!map.TryGetValue(name, out var item)) continue;
                _path.Add((name, 0));
                if (item is not DataModelNull) Check(schema, context, item);
                else if (!nullable.Contains(name)) Error("Must not be null.");
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void CheckArray(LexArray array, Nsid context, DataModelValue value)
        {
            if (Expect<DataModelArray>(value, "an array") is not { } items) return;
            Length(items.Count, array.MinLength, array.MaxLength, "items");
            if (TooDeep()) return;
            for (var i = 0; i < items.Count && !Full; i++)
            {
                _path.Add((null, i));
                Check(array.Items, context, items[i]);
                _path.RemoveAt(_path.Count - 1);
            }
        }

        private void CheckRef(string reference, Nsid context, DataModelValue value)
        {
            if (!catalog.TryResolve(reference, context, out var resolved)) Error($"The reference \"{reference}\" does not resolve.");
            else if (resolved.Definition is not null)
            {
                // A record always carries its $type, in union position or not (LEX-18).
                if (resolved.Definition is LexRecord && resolved.Target.DefName == "main") CheckRecordType(resolved.Target.Nsid, value);
                Check(resolved.Definition, resolved.Target.Nsid, value);
            }
        }

        // LEX-19: a member needs its $type; an unlisted one passes an open union only.
        private void CheckUnion(LexUnion union, Nsid context, DataModelValue value)
        {
            if (Expect<DataModelMap>(value, "an object") is not { } map) return;
            if (TypeOf(map) is not { } type)
            {
                Add(PathTo("$type"), "A union member has a $type.");
                return;
            }
            var actual = ParseType(type);
            foreach (var reference in union.Refs)
            {
                if (!LexRefTarget.TryParse(reference, context, out var member) || member != actual) continue;
                if (!catalog.TryResolve(member, out var resolved)) Error($"The union member {member} does not resolve.");
                // A union in a union could check the same value forever (the catalog reports it, LEX-7).
                else if (resolved.Definition is LexUnion) Error($"The union member {member} is a union.");
                else if (resolved.Definition is not null) Check(resolved.Definition, resolved.Target.Nsid, value);
                return;
            }
            if (union.Closed) Add(PathTo("$type"), $"\"{type}\" is not a member of the closed union.");
        }

        private void CheckString(LexString schema, DataModelValue value)
        {
            if (Expect<DataModelString>(value, "a string") is not { Value: var text }) return;
            if (schema.Const is { } constant && text != constant) Error($"Must be \"{constant}\".");
            if (schema.Enum is { } allowed && !allowed.Contains(text)) Error($"Must be one of {string.Join(", ", allowed.Select(a => $"\"{a}\""))}.");
            if (schema.MinLength is not null || schema.MaxLength is not null)
                Length(Encoding.UTF8.GetByteCount(text), schema.MinLength, schema.MaxLength, "UTF-8 bytes");
            if (schema.MinGraphemes is not null || schema.MaxGraphemes is not null)
                Length(new StringInfo(text).LengthInTextElements, schema.MinGraphemes, schema.MaxGraphemes, "graphemes");
            if (schema.Format is { } format && !IsFormat(format, text)) Error($"Is not a valid {format}.");
        }

        private void CheckInteger(LexInteger schema, DataModelValue value)
        {
            if (Expect<DataModelInteger>(value, "an integer") is not { Value: var number }) return;
            if (schema.Const is { } constant && number != constant) Error($"Must be {constant}.");
            if (schema.Enum is { } allowed && !allowed.Contains(number)) Error($"Must be one of {string.Join(", ", allowed)}.");
            if (number < schema.Minimum) Error($"Is {number}; the minimum is {schema.Minimum}.");
            if (number > schema.Maximum) Error($"Is {number}; the maximum is {schema.Maximum}.");
        }

        private void CheckBlob(LexBlob schema, DataModelValue value)
        {
            if (value is not DataModelMap map || !TryBlob(map, out var mimeType, out var size))
            {
                Error($"Expected a blob, got {KindOf(value)}.");
                return;
            }
            if (schema.Accept is { } accept && !accept.Any(pattern => Accepts(pattern, mimeType))) Error($"The MIME type \"{mimeType}\" is not accepted.");
            if (size > schema.MaxSize) Error($"Is {size} bytes; the maximum is {schema.MaxSize}.");
        }

        private T? Expect<T>(DataModelValue value, string kind) where T : DataModelValue
        {
            if (value is T typed) return typed;
            Error($"Expected {kind}, got {KindOf(value)}.");
            return null;
        }

        private void Length(long actual, long? min, long? max, string unit)
        {
            if (actual < min) Error($"Has {actual} {unit}; the minimum is {min}.");
            if (actual > max) Error($"Has {actual} {unit}; the maximum is {max}.");
        }

        private bool TooDeep()
        {
            if (_path.Count < LexiconLimits.MaxValidationDepth) return false;
            Error($"Nested deeper than {LexiconLimits.MaxValidationDepth} levels; not checked further.");
            return true;
        }

        private void Error(string message) => Add(PathTo(null), message);

        // $.embed.images[0].alt; a name that is not a plain identifier in brackets: $['cid-link'].
        private string PathTo(string? last)
        {
            if (Full) return "";
            var builder = new StringBuilder("$");
            foreach (var (name, index) in _path) Append(builder, name, index);
            if (last is not null) Append(builder, last, 0);
            return builder.ToString();
        }

        private static void Append(StringBuilder builder, string? name, int index)
        {
            if (name is null) builder.Append('[').Append(index.ToString(CultureInfo.InvariantCulture)).Append(']');
            else if (name.Length > 0 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '$')) builder.Append('.').Append(name);
            else builder.Append("['").Append(name.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("'", "\\'", StringComparison.Ordinal)).Append("']");
        }
    }

    private static string? TypeOf(DataModelMap map) => map.TryGetValue("$type", out var type) && type is DataModelString { Value: var text } ? text : null;

    // A $type names a definition absolutely: "nsid" (its main) or "nsid#name".
    private static LexRefTarget? ParseType(string type)
    {
        var hash = type.IndexOf('#');
        return hash != 0 && Nsid.TryParse(hash < 0 ? type : type[..hash], out var nsid) && LexRefTarget.TryParse(type, nsid, out var target) ? target : null;
    }

    private static bool IsFormat(string format, string text) => format switch
    {
        "at-identifier" => AtIdentifier.TryParse(text, out _),
        "at-uri" => AtUri.TryParse(text, out _),
        "cid" => Cid.TryParse(text, out _),
        "datetime" => AtDatetime.TryParse(text, out _),
        "did" => Did.TryParse(text, out _),
        "handle" => Handle.TryParse(text, out _),
        "language" => Language.TryParse(text, out _),
        "nsid" => Nsid.TryParse(text, out _),
        "record-key" => RecordKey.TryParse(text, out _),
        "space-ref" => SpaceUri.TryParse(text, out _),
        "tid" => Tid.TryParse(text, out _),
        "uri" => UriFormat.IsValid(text),
        _ => true, // a format this version does not know (LEX-19)
    };

    // The data model's blob, or the legacy {"cid", "mimeType"} form it still reads.
    private static bool TryBlob(DataModelMap map, out string mimeType, out long? size)
    {
        mimeType = map.TryGetValue("mimeType", out var mime) && mime is DataModelString { Value.Length: > 0 } text ? text.Value : "";
        size = map.TryGetValue("size", out var length) && length is DataModelInteger { Value: >= 0 } number ? number.Value : null;
        if (mimeType.Length == 0) return false;
        if (TypeOf(map) == "blob") return size is not null && map.TryGetValue("ref", out var link) && link is DataModelLink;
        return map.Count == 2 && map.TryGetValue("cid", out var cid) && cid is DataModelString { Value: var cidText } && Cid.TryParse(cidText, out _);
    }

    private static bool IsBlob(DataModelMap map) => TypeOf(map) == "blob" || TryBlob(map, out _, out _);

    // An accept pattern is a MIME type, a type ending in * (image/*), or */* for any.
    private static bool Accepts(string pattern, string mimeType) =>
        pattern == "*/*" || (pattern.EndsWith('*')
            ? mimeType.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase)
            : string.Equals(pattern, mimeType, StringComparison.OrdinalIgnoreCase));

    private static string KindOf(DataModelValue value) => value switch
    {
        DataModelNull => "null",
        DataModelBoolean => "a boolean",
        DataModelInteger => "an integer",
        DataModelString => "a string",
        DataModelBytes => "bytes",
        DataModelLink => "a link",
        DataModelArray => "an array",
        DataModelMap map => IsBlob(map) ? "a blob" : "an object",
    };
}
