using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

// Linked into ATProtoNet.Lexicon.CodeGen (LEX-26).
namespace ATProtoNet.Lexicon;

/// <summary>
/// The lint rules a published schema follows beyond what parsing enforces: permission sets, space declarations and the Lexicon
/// style guide (LEX-10–LEX-13). The CLI's <c>lint</c> and <c>publish</c> and the generator run the same rules.
/// </summary>
/// <remarks>
/// Permission sets follow <see href="https://atproto.com/specs/permission#permission-sets"/>: an authorization server silently
/// drops a permission that breaks a rule, so each is an error here rather than a surprise at consent time.
/// </remarks>
static partial class LexiconLint
{
    /// <summary>Lints every document of a catalog.</summary>
    /// <param name="catalog">The documents.</param>
    /// <returns>The findings, sorted by NSID and JSON pointer.</returns>
    public static IReadOnlyList<LexiconDiagnostic> Run(LexiconCatalog catalog)
    {
        var diagnostics = new List<LexiconDiagnostic>();
        foreach (var document in catalog.Documents) Lint(document, diagnostics);
        return [.. diagnostics.OrderBy(d => d.DocumentId, StringComparer.Ordinal).ThenBy(d => d.Pointer, StringComparer.Ordinal)];
    }

    private static void Lint(LexiconDocument document, List<LexiconDiagnostic> diagnostics)
    {
        void Report(string rule, LexiconSeverity severity, string message, string pointer) =>
            diagnostics.Add(new(rule, severity, message, document.Id.ToString(), pointer));

        foreach (var def in document.Defs)
        {
            var pointer = "/defs/" + LexiconParser.Escape(def.Key);
            switch (def.Value)
            {
                case LexPermissionSet set:
                    PermissionSet(document.Id, set, pointer, Report);
                    break;
                case LexSpace space:
                    for (var i = 0; i < space.Collections.Count; i++)
                        if (space.Collections[i] == "*" || space.Collections[i].EndsWith(".*", StringComparison.Ordinal))
                            Report(LexiconRules.SpaceCollection, LexiconSeverity.Warning, "\"collections\" lists the collections a space holds, not a wildcard.", pointer + "/collections/" + i);
                        else if (!Nsid.TryParse(space.Collections[i], out _))
                            Report(LexiconRules.SpaceCollection, LexiconSeverity.Warning, $"The collection \"{space.Collections[i]}\" is not an NSID.", pointer + "/collections/" + i);
                    break;
            }
            if (def.Value is LexRecord or LexQuery or LexProcedure or LexSubscription or LexPermissionSet or LexSpace && string.IsNullOrWhiteSpace(def.Value.Description))
                Report(LexiconRules.StyleDescription, LexiconSeverity.Warning, "A primary definition has a description.", pointer);
            Style(def.Value, pointer, Report);
        }
    }

    private static void Style(LexSchema schema, string pointer, Action<string, LexiconSeverity, string, string> report)
    {
        switch (schema)
        {
            case LexString { MaxGraphemes: not null, MaxLength: null }:
                report(LexiconRules.StyleGraphemes, LexiconSeverity.Warning, "A string with \"maxGraphemes\" also has a \"maxLength\".", pointer);
                break;
            case LexRecord record:
                Style(record.Record, pointer + "/record", report);
                break;
            case LexObject obj:
                Properties(obj.Properties, pointer, report);
                break;
            case LexParams parameters:
                Properties(parameters.Properties, pointer, report);
                break;
            case LexArray array:
                Style(array.Items, pointer + "/items", report);
                break;
            case LexQuery query:
                if (query.Parameters is { } queryParameters) Style(queryParameters, pointer + "/parameters", report);
                if (query.Output?.Schema is { } output) Style(output, pointer + "/output/schema", report);
                break;
            case LexProcedure procedure:
                if (procedure.Parameters is { } procedureParameters) Style(procedureParameters, pointer + "/parameters", report);
                if (procedure.Input?.Schema is { } input) Style(input, pointer + "/input/schema", report);
                if (procedure.Output?.Schema is { } result) Style(result, pointer + "/output/schema", report);
                break;
            case LexSubscription subscription:
                if (subscription.Parameters is { } subscriptionParameters) Style(subscriptionParameters, pointer + "/parameters", report);
                if (subscription.Message?.Schema is { } message) Style(message, pointer + "/message/schema", report);
                break;
        }
    }

    private static void Properties(IReadOnlyDictionary<string, LexSchema> properties, string pointer, Action<string, LexiconSeverity, string, string> report)
    {
        foreach (var property in properties)
        {
            var at = pointer + "/properties/" + LexiconParser.Escape(property.Key);
            if (!IsCamelCase(property.Key))
                report(LexiconRules.StylePropertyName, LexiconSeverity.Warning, $"The property name \"{property.Key}\" is not camelCase.", at);
            Style(property.Value, at, report);
        }
    }

    // Lexicon style guide: lowerCamelCase, ASCII letters and digits.
    private static bool IsCamelCase(string name) => name.Length > 0 && name[0] is >= 'a' and <= 'z' && name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9');

    // LEX-11.
    private static void PermissionSet(Nsid id, LexPermissionSet set, string pointer, Action<string, LexiconSeverity, string, string> report)
    {
        if (string.IsNullOrWhiteSpace(set.Title))
            report(LexiconRules.PermissionSetShape, LexiconSeverity.Error, "A permission set has a \"title\" for consent screens.", pointer);
        if (set.Permissions.Count == 0)
            report(LexiconRules.PermissionSetShape, LexiconSeverity.Error, "A permission set grants at least one permission.", pointer + "/permissions");
        for (var i = 0; i < set.Permissions.Count; i++)
        {
            var at = pointer + "/permissions/" + i;
            var raw = set.Permissions[i].Raw;
            void Error(string message) => report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, message, at);
            if (raw.ValueKind != JsonValueKind.Object)
            {
                Error("A permission is an object.");
                continue;
            }
            if (!raw.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String || type.GetString() != "permission")
                Error("A permission's \"type\" is \"permission\".");
            switch (set.Permissions[i].Resource)
            {
                case null:
                    Error("A permission has a string \"resource\".");
                    break;
                case "blob" or "account" or "identity":
                    Error($"\"{set.Permissions[i].Resource}\" permissions cannot be part of a permission set; request them as scopes directly.");
                    break;
                case "repo":
                    Nsids(id, raw, "collection", at, report);
                    Actions(raw, at, report);
                    NotApplicable(raw, ["type", "resource", "collection", "action"], "repo", at, report);
                    break;
                case "rpc":
                    Nsids(id, raw, "lxm", at, report);
                    Audience(raw, at, report);
                    NotApplicable(raw, ["type", "resource", "lxm", "aud", "inheritAud"], "rpc", at, report);
                    break;
                default:
                    report(LexiconRules.PermissionIgnored, LexiconSeverity.Warning,
                        $"Authorization servers ignore \"{set.Permissions[i].Resource}\" permissions in a permission set.", at + "/resource");
                    break;
            }
        }
    }

    // The collection or lxm list: required, no wildcards, and only NSIDs in the set's own group or below it.
    private static void Nsids(Nsid id, JsonElement permission, string field, string pointer, Action<string, LexiconSeverity, string, string> report)
    {
        pointer += "/" + field;
        if (!permission.TryGetProperty(field, out var values) || values.ValueKind != JsonValueKind.Array || values.GetArrayLength() == 0)
        {
            report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, $"\"{field}\" is a non-empty array of NSIDs.", pointer);
            return;
        }
        var group = id.ToString().Substring(0, id.ToString().LastIndexOf('.') + 1);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in values.EnumerateArray())
        {
            var at = pointer + "/" + index++;
            var value = item.ValueKind == JsonValueKind.String ? item.GetString()! : null;
            if (value is null)
                report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, $"\"{field}\" lists strings.", at);
            else if (value == "*" || value.EndsWith(".*", StringComparison.Ordinal))
                report(LexiconRules.PermissionScope, LexiconSeverity.Error, $"A permission set cannot use wildcards (\"{value}\").", at);
            else if (!Nsid.TryParse(value, out _))
                report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, $"\"{value}\" is not an NSID.", at);
            else if (!value.StartsWith(group, StringComparison.Ordinal))
                report(LexiconRules.PermissionScope, LexiconSeverity.Error, $"\"{value}\" is outside the permission set's namespace \"{group.TrimEnd('.')}\".", at);
            if (value is not null && !seen.Add(value))
                report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, $"\"{field}\" lists \"{value}\" twice.", at);
        }
    }

    private static void Actions(JsonElement permission, string pointer, Action<string, LexiconSeverity, string, string> report)
    {
        if (!permission.TryGetProperty("action", out var actions)) return;
        pointer += "/action";
        if (actions.ValueKind != JsonValueKind.Array)
        {
            report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, "\"action\" is an array.", pointer);
            return;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        foreach (var item in actions.EnumerateArray())
        {
            var at = pointer + "/" + index++;
            var action = item.ValueKind == JsonValueKind.String ? item.GetString()! : "";
            if (action is not ("create" or "update" or "delete"))
                report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, $"\"{action}\" is not one of create, update, delete.", at);
            else if (!seen.Add(action))
                report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, $"\"action\" lists \"{action}\" twice.", at);
        }
    }

    // An rpc audience in a permission set: "*", or none with inheritAud; never a service reference, never both.
    private static void Audience(JsonElement permission, string pointer, Action<string, LexiconSeverity, string, string> report)
    {
        var inherit = permission.TryGetProperty("inheritAud", out var inheritAud) && inheritAud.ValueKind == JsonValueKind.True;
        var hasAud = permission.TryGetProperty("aud", out var aud);
        var message = (inherit, hasAud) switch
        {
            (true, true) => "\"aud\" and \"inheritAud\" cannot both be set.",
            (false, false) => "\"aud\" is required unless \"inheritAud\" is true.",
            (false, true) when aud.ValueKind != JsonValueKind.String || aud.GetString() != "*" =>
                "\"aud\" is \"*\" in a permission set; use \"inheritAud\" to take the audience from the include scope.",
            _ => null,
        };
        if (message is not null) report(LexiconRules.PermissionInvalid, LexiconSeverity.Error, message, pointer + "/aud");
    }

    private static void NotApplicable(JsonElement permission, string[] known, string resource, string pointer, Action<string, LexiconSeverity, string, string> report)
    {
        foreach (var parameter in permission.EnumerateObject())
            if (Array.IndexOf(known, parameter.Name) < 0)
                report(LexiconRules.PermissionParameter, LexiconSeverity.Warning,
                    $"\"{parameter.Name}\" does not apply to \"{resource}\" permissions; authorization servers ignore a permission with parameters they do not know.",
                    pointer + "/" + LexiconParser.Escape(parameter.Name));
    }
}
