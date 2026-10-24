using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;

namespace ATProtoNet.Lexicon;

public static partial class LexiconCompatibility
{
    // Compares two versions of one document node by node, in the same JSON pointer layout the parser reports.
    private sealed class Differ(Nsid id, List<LexiconChange> changes)
    {
        public void Document(LexiconDocument before, LexiconDocument after)
        {
            Text("/description", "description", before.Description, after.Description);
            foreach (var (name, def) in before.Defs)
            {
                var at = "/defs/" + LexiconParser.Escape(name);
                if (after.Defs.TryGetValue(name, out var next)) Schema(def, next, at);
                else Add(at, LexiconChangeKind.Removed, true, $"The definition \"{name}\" was removed.");
            }
            foreach (var name in after.Defs.Keys)
                if (!before.Defs.ContainsKey(name))
                    Add("/defs/" + LexiconParser.Escape(name), LexiconChangeKind.Added, false, $"The definition \"{name}\" was added.");
        }

        private void Add(string pointer, LexiconChangeKind kind, bool breaking, string message) =>
            changes.Add(new(id, pointer, kind, breaking ? LexiconChangeClassification.Breaking : LexiconChangeClassification.Compatible, message));

        private void Schema(LexSchema before, LexSchema after, string pointer)
        {
            if (before.GetType() != after.GetType())
            {
                Add(pointer, LexiconChangeKind.Changed, true, $"The type changed from \"{TypeName(before)}\" to \"{TypeName(after)}\".");
                return;
            }
            Text(pointer + "/description", "description", before.Description, after.Description);
            switch (before, after)
            {
                case (LexRecord b, LexRecord a):
                    if (b.Key != a.Key) Add(pointer + "/key", LexiconChangeKind.Changed, true, $"The record key changed from \"{b.Key}\" to \"{a.Key}\".");
                    Schema(b.Record, a.Record, pointer + "/record");
                    break;
                case (LexObject b, LexObject a):
                    Properties(pointer, b.Properties, a.Properties, b.Required, a.Required, b.Nullable, a.Nullable);
                    break;
                case (LexParams b, LexParams a):
                    Properties(pointer, b.Properties, a.Properties, b.Required, a.Required, [], []);
                    break;
                case (LexQuery b, LexQuery a):
                    Parameters(b.Parameters, a.Parameters, pointer);
                    Body("output", b.Output, a.Output, pointer, false);
                    Errors(b.Errors, a.Errors, pointer);
                    break;
                case (LexProcedure b, LexProcedure a):
                    Parameters(b.Parameters, a.Parameters, pointer);
                    Body("input", b.Input, a.Input, pointer, true);
                    Body("output", b.Output, a.Output, pointer, false);
                    Errors(b.Errors, a.Errors, pointer);
                    break;
                case (LexSubscription b, LexSubscription a):
                    Parameters(b.Parameters, a.Parameters, pointer);
                    Body("message", b.Message, a.Message, pointer, false);
                    Errors(b.Errors, a.Errors, pointer);
                    break;
                case (LexPermissionSet b, LexPermissionSet a):
                    PermissionSet(b, a, pointer);
                    break;
                case (LexSpace b, LexSpace a):
                    if (b.Key != a.Key) Add(pointer + "/key", LexiconChangeKind.Changed, true, $"The space key changed from \"{b.Key}\" to \"{a.Key}\".");
                    Text(pointer + "/name", "name", b.Name, a.Name);
                    Localized(pointer, "name:lang", b.NameLang, a.NameLang);
                    // A bare space grant covers the collections as declared when it is evaluated, so adding one widens grants
                    // already consented to: compatible, but never silent.
                    Members(pointer + "/collections", "The collection", b.Collections, a.Collections, false);
                    break;
                case (LexString b, LexString a):
                    Constraint(pointer, "format", b.Format, a.Format);
                    Constraint(pointer, "minLength", b.MinLength, a.MinLength);
                    Constraint(pointer, "maxLength", b.MaxLength, a.MaxLength);
                    Constraint(pointer, "minGraphemes", b.MinGraphemes, a.MinGraphemes);
                    Constraint(pointer, "maxGraphemes", b.MaxGraphemes, a.MaxGraphemes);
                    Constraint(pointer, "const", b.Const, a.Const);
                    Constraint(pointer, "default", b.Default, a.Default);
                    Values(pointer, "enum", b.Enum, a.Enum, true);
                    Values(pointer, "knownValues", b.KnownValues, a.KnownValues, false);
                    break;
                case (LexInteger b, LexInteger a):
                    Constraint(pointer, "minimum", b.Minimum, a.Minimum);
                    Constraint(pointer, "maximum", b.Maximum, a.Maximum);
                    Constraint(pointer, "const", b.Const, a.Const);
                    Constraint(pointer, "default", b.Default, a.Default);
                    Values(pointer, "enum", b.Enum?.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList(), a.Enum?.Select(v => v.ToString(CultureInfo.InvariantCulture)).ToList(), true);
                    break;
                case (LexBoolean b, LexBoolean a):
                    Constraint(pointer, "const", b.Const, a.Const);
                    Constraint(pointer, "default", b.Default, a.Default);
                    break;
                case (LexBytes b, LexBytes a):
                    Constraint(pointer, "minLength", b.MinLength, a.MinLength);
                    Constraint(pointer, "maxLength", b.MaxLength, a.MaxLength);
                    break;
                case (LexBlob b, LexBlob a):
                    Values(pointer, "accept", b.Accept, a.Accept, true);
                    Constraint(pointer, "maxSize", b.MaxSize, a.MaxSize);
                    break;
                case (LexArray b, LexArray a):
                    Constraint(pointer, "minLength", b.MinLength, a.MinLength);
                    Constraint(pointer, "maxLength", b.MaxLength, a.MaxLength);
                    Schema(b.Items, a.Items, pointer + "/items");
                    break;
                case (LexRef b, LexRef a):
                    if (Target(b.Ref) != Target(a.Ref)) Add(pointer + "/ref", LexiconChangeKind.Changed, true, $"The reference changed from \"{Target(b.Ref)}\" to \"{Target(a.Ref)}\".");
                    break;
                case (LexUnion b, LexUnion a):
                    if (b.Closed != a.Closed) Add(pointer + "/closed", LexiconChangeKind.Changed, true, $"The union changed from {(b.Closed ? "closed" : "open")} to {(a.Closed ? "closed" : "open")}.");
                    Members(pointer + "/refs", "The union member", [.. b.Refs.Select(Target)], [.. a.Refs.Select(Target)], a.Closed);
                    break;
            }
        }

        private static string TypeName(LexSchema schema) => schema switch
        {
            LexRecord => "record",
            LexQuery => "query",
            LexProcedure => "procedure",
            LexSubscription => "subscription",
            LexPermissionSet => "permission-set",
            LexSpace => "space",
            LexToken => "token",
            LexObject => "object",
            LexParams => "params",
            LexString => "string",
            LexInteger => "integer",
            LexBoolean => "boolean",
            LexBytes => "bytes",
            LexCidLink => "cid-link",
            LexBlob => "blob",
            LexArray => "array",
            LexUnknown => "unknown",
            LexRef => "ref",
            LexUnion => "union",
            LexUnknownType unknown => unknown.TypeName,
            _ => schema.GetType().Name,
        };

        // "#main" and "nsid" and "nsid#main" are the same reference.
        private string Target(string reference) => LexRefTarget.TryParse(reference, id, out var target) ? target.ToString() : reference;

        private void Properties(
            string pointer, IReadOnlyDictionary<string, LexSchema> before, IReadOnlyDictionary<string, LexSchema> after,
            IReadOnlyList<string> beforeRequired, IReadOnlyList<string> afterRequired, IReadOnlyList<string> beforeNullable, IReadOnlyList<string> afterNullable)
        {
            foreach (var (name, schema) in before)
            {
                var at = pointer + "/properties/" + LexiconParser.Escape(name);
                if (!after.TryGetValue(name, out var next))
                {
                    Add(at, LexiconChangeKind.Removed, true, $"The property \"{name}\" was removed.");
                    continue;
                }
                var required = afterRequired.Contains(name);
                if (beforeRequired.Contains(name) != required)
                    Add(at, LexiconChangeKind.Changed, true, $"The property \"{name}\" changed from {(required ? "optional to required" : "required to optional")}.");
                var nullable = afterNullable.Contains(name);
                if (beforeNullable.Contains(name) != nullable)
                    Add(at, LexiconChangeKind.Changed, true, $"The property \"{name}\" is {(nullable ? "now" : "no longer")} nullable.");
                Schema(schema, next, at);
            }
            foreach (var name in after.Keys)
                if (!before.ContainsKey(name))
                {
                    var required = afterRequired.Contains(name);
                    Add(pointer + "/properties/" + LexiconParser.Escape(name), LexiconChangeKind.Added, required, $"The {(required ? "required" : "optional")} property \"{name}\" was added.");
                }
        }

        private void Parameters(LexParams? before, LexParams? after, string pointer)
        {
            pointer += "/parameters";
            if (before is not null && after is not null) Schema(before, after, pointer);
            else Properties(pointer, before?.Properties ?? FrozenDictionary<string, LexSchema>.Empty, after?.Properties ?? FrozenDictionary<string, LexSchema>.Empty,
                before?.Required ?? [], after?.Required ?? [], [], []);
        }

        // A removed body breaks callers; a new request body breaks them too, a new response body does not.
        private void Body(string name, LexBody? before, LexBody? after, string pointer, bool addedBreaks)
        {
            pointer += "/" + name;
            if (before is null && after is null) return;
            if (before is null || after is null)
            {
                Add(pointer, before is null ? LexiconChangeKind.Added : LexiconChangeKind.Removed, before is null ? addedBreaks : true, $"The {name} was {(before is null ? "added" : "removed")}.");
                return;
            }
            if (before.Encoding != after.Encoding)
                Add(pointer + "/encoding", LexiconChangeKind.Changed, true, $"The {name} encoding changed from \"{before.Encoding}\" to \"{after.Encoding}\".");
            Text(pointer + "/description", "description", before.Description, after.Description);
            if (before.Schema is not null && after.Schema is not null) Schema(before.Schema, after.Schema, pointer + "/schema");
            else if (before.Schema is not null || after.Schema is not null)
                Add(pointer + "/schema", before.Schema is null ? LexiconChangeKind.Added : LexiconChangeKind.Removed, before.Schema is null ? addedBreaks : true,
                    $"The {name} schema was {(before.Schema is null ? "added" : "removed")}.");
        }

        // Declaring an error is documentation: servers may return undeclared ones, so neither adding nor removing one breaks anyone.
        private void Errors(IReadOnlyList<LexError> before, IReadOnlyList<LexError> after, string pointer)
        {
            pointer += "/errors";
            for (var i = 0; i < before.Count; i++)
            {
                var match = after.FirstOrDefault(e => e.Name == before[i].Name);
                if (match is null) Add($"{pointer}/{i}", LexiconChangeKind.Removed, false, $"The error \"{before[i].Name}\" is no longer declared.");
                else Text($"{pointer}/{i}/description", $"description of the error \"{match.Name}\"", before[i].Description, match.Description);
            }
            for (var i = 0; i < after.Count; i++)
                if (before.All(e => e.Name != after[i].Name)) Add($"{pointer}/{i}", LexiconChangeKind.Added, false, $"The error \"{after[i].Name}\" was declared.");
        }

        // Like space collections, the permissions grant what the set says when it is evaluated: adding widens, removing narrows.
        private void PermissionSet(LexPermissionSet before, LexPermissionSet after, string pointer)
        {
            Text(pointer + "/title", "title", before.Title, after.Title);
            Text(pointer + "/detail", "detail", before.Detail, after.Detail);
            Localized(pointer, "title:lang", before.TitleLang, after.TitleLang);
            Localized(pointer, "detail:lang", before.DetailLang, after.DetailLang);
            for (var i = 0; i < before.Permissions.Count; i++)
                if (!after.Permissions.Any(p => JsonElement.DeepEquals(p.Raw, before.Permissions[i].Raw)))
                    Add($"{pointer}/permissions/{i}", LexiconChangeKind.Removed, true, "A permission was removed or changed.");
            for (var i = 0; i < after.Permissions.Count; i++)
                if (!before.Permissions.Any(p => JsonElement.DeepEquals(p.Raw, after.Permissions[i].Raw)))
                    Add($"{pointer}/permissions/{i}", LexiconChangeKind.Added, false, "A permission was added.");
        }

        // Titles, names and descriptions are for people.
        private void Text(string pointer, string label, string? before, string? after)
        {
            if (before == after) return;
            var kind = before is null ? LexiconChangeKind.Added : after is null ? LexiconChangeKind.Removed : LexiconChangeKind.Changed;
            Add(pointer, kind, false, $"The {label} was {kind.ToString().ToLowerInvariant()}.");
        }

        private void Localized(string pointer, string name, IReadOnlyDictionary<string, string>? before, IReadOnlyDictionary<string, string>? after)
        {
            foreach (var language in (before?.Keys ?? []).Union(after?.Keys ?? []))
                Text($"{pointer}/{name}/{LexiconParser.Escape(language)}", $"{name} for \"{language}\"", before?.GetValueOrDefault(language), after?.GetValueOrDefault(language));
        }

        // LEX-15: a constraint on a previously unconstrained field is compatible; dropping or changing one, tightening or loosening, is not.
        private void Constraint(string pointer, string name, object? before, object? after)
        {
            if (Equals(before, after)) return;
            var at = pointer + "/" + name;
            if (before is null) Add(at, LexiconChangeKind.Added, false, $"\"{name}\" ({after}) was added.");
            else if (after is null) Add(at, LexiconChangeKind.Removed, true, $"\"{name}\" ({before}) was removed.");
            else Add(at, LexiconChangeKind.Changed, true, $"\"{name}\" changed from {before} to {after}.");
        }

        // enum and accept are closed sets, so any change breaks; knownValues only suggests.
        private void Values(string pointer, string name, IReadOnlyList<string>? before, IReadOnlyList<string>? after, bool strict)
        {
            pointer += "/" + name;
            if (before is not null && after is not null) Members(pointer, $"The {name} value", before, after, strict, strict);
            else if (before is not null) Add(pointer, LexiconChangeKind.Removed, strict, $"\"{name}\" was removed.");
            else if (after is not null) Add(pointer, LexiconChangeKind.Added, false, $"\"{name}\" was added.");
        }

        private void Members(string pointer, string noun, IReadOnlyList<string> before, IReadOnlyList<string> after, bool addedBreaks, bool removedBreaks = true)
        {
            for (var i = 0; i < before.Count; i++)
                if (!after.Contains(before[i])) Add($"{pointer}/{i}", LexiconChangeKind.Removed, removedBreaks, $"{noun} \"{before[i]}\" was removed.");
            for (var i = 0; i < after.Count; i++)
                if (!before.Contains(after[i])) Add($"{pointer}/{i}", LexiconChangeKind.Added, addedBreaks, $"{noun} \"{after[i]}\" was added.");
        }
    }
}
