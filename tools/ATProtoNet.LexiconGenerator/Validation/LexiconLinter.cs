using ATProtoNet.Identity;
using ATProtoNet.LexiconGenerator.Schema;

namespace ATProtoNet.LexiconGenerator.Validation;

/// <summary>How serious a <see cref="LintDiagnostic"/> is.</summary>
public enum LintSeverity
{
    /// <summary>Valid, but probably not what the author meant, or ignored by some consumers.</summary>
    Warning,

    /// <summary>Invalid under the Lexicon or permission specification.</summary>
    Error,
}

/// <summary>One finding of <see cref="LexiconLinter"/>.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Nsid">The document's NSID.</param>
/// <param name="Definition">The definition it concerns, or <see langword="null"/> for the document.</param>
/// <param name="Message">What is wrong.</param>
public sealed record LintDiagnostic(LintSeverity Severity, string Nsid, string? Definition, string Message)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Nsid}{(Definition is null ? "" : "#" + Definition)}: {Message}";
}

/// <summary>
/// Checks Lexicon documents against the rules the code generator does not need but a published
/// schema must follow: the document's shape, the removed <c>null</c> type, <c>space</c>
/// declarations, and — in most detail — <c>permission-set</c> definitions.
/// </summary>
/// <remarks>
/// Permission sets follow https://atproto.com/specs/permission#permission-sets: only <c>repo</c>
/// and <c>rpc</c> permissions, no wildcards, only resources under the set's own NSID namespace,
/// and an <c>rpc</c> audience that is either <c>*</c> or inherited from the <c>include:</c> scope.
/// An authorization server silently drops a permission that breaks these rules, so each is an
/// error here rather than a surprise at consent time.
/// </remarks>
public static class LexiconLinter
{
    private static readonly HashSet<string> PrimaryTypes = new(StringComparer.Ordinal)
    {
        "record", "query", "procedure", "subscription", "permission-set", "space",
    };

    private static readonly HashSet<string> DefinitionTypes = new(PrimaryTypes, StringComparer.Ordinal)
    {
        "object", "array", "token", "string", "integer", "boolean", "bytes", "cid-link", "blob",
        // Not allowed as named definitions by the specification, but used by published schemas.
        "ref", "union", "unknown",
    };

    private static readonly HashSet<string> RepoActions = new(StringComparer.Ordinal) { "create", "update", "delete" };

    /// <summary>Lints one document.</summary>
    /// <param name="document">The document.</param>
    /// <returns>The findings, errors and warnings in document order.</returns>
    public static IReadOnlyList<LintDiagnostic> Lint(LexiconDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var diagnostics = new List<LintDiagnostic>();
        var nsid = document.Id;

        void Report(LintSeverity severity, string? def, string message) =>
            diagnostics.Add(new LintDiagnostic(severity, nsid, def, message));

        if (!Nsid.TryParse(nsid, out var parsedId))
            Report(LintSeverity.Error, null, "'id' is not a valid NSID");

        if (document.Defs.Count == 0)
            Report(LintSeverity.Error, null, "the document has no definitions");

        foreach (var (name, def) in document.Defs)
        {
            if (def.Type == "null")
            {
                Report(LintSeverity.Error, name, "the 'null' type was removed from the Lexicon language");
                continue;
            }

            if (!DefinitionTypes.Contains(def.Type))
            {
                Report(LintSeverity.Warning, name, def.Type.Length == 0
                    ? "the definition has no 'type'"
                    : $"'{def.Type}' is not a Lexicon definition type");
                continue;
            }

            if (PrimaryTypes.Contains(def.Type) && name != "main")
                Report(LintSeverity.Error, name, $"a '{def.Type}' definition must be named 'main'");

            FindNullTypes(def, name, Report);

            switch (def.Type)
            {
                case "permission-set" when parsedId is not null:
                    LintPermissionSet(parsedId, def, name, Report);
                    break;
                case "space":
                    LintSpace(def, name, Report);
                    break;
            }
        }

        return diagnostics;
    }

    private static void LintSpace(LexiconSchema def, string name, Action<LintSeverity, string?, string> report)
    {
        // The space proposal sets no limit on 'name', so its length is not checked; 'collections'
        // may span NSID domains but must list NSIDs, never a wildcard.
        foreach (var collection in def.Collections ?? [])
        {
            if (collection == "*")
                report(LintSeverity.Warning, name, "'collections' must not contain '*'; list the collections a space holds");
            else if (!Nsid.TryParse(collection, out _))
                report(LintSeverity.Warning, name, $"'collections' entry '{collection}' is not an NSID");
        }
    }

    private static void LintPermissionSet(
        Nsid setNsid, LexiconSchema def, string name, Action<LintSeverity, string?, string> report)
    {
        if (def.Permissions is null)
        {
            report(LintSeverity.Error, name, "'permissions' is required");
            return;
        }

        if (def.Permissions.Count == 0)
            report(LintSeverity.Warning, name, "the permission set grants nothing");

        if (string.IsNullOrWhiteSpace(def.Title))
            report(LintSeverity.Warning, name, "no 'title': consent screens can only show the NSID");

        for (var i = 0; i < def.Permissions.Count; i++)
        {
            var permission = def.Permissions[i];
            var at = $"permissions[{i}]";

            void Error(string message) => report(LintSeverity.Error, name, $"{at}: {message}");
            void Warn(string message) => report(LintSeverity.Warning, name, $"{at}: {message}");

            if (permission.Type != "permission")
                Error($"'type' must be 'permission', not '{permission.Type}'");

            switch (permission.Resource)
            {
                case "":
                    Error("'resource' is required");
                    continue;

                case "blob" or "account" or "identity":
                    Error($"'{permission.Resource}' permissions cannot be part of a permission set; request them as scopes directly");
                    continue;

                case "repo":
                    CheckNsids(setNsid, permission.Collection, "collection", Error);
                    CheckActions(permission.Action, Error);
                    CheckNotApplicable(permission, "repo", Warn, ("lxm", permission.Lxm), ("aud", permission.Aud), ("inheritAud", permission.InheritAud));
                    break;

                case "rpc":
                    CheckNsids(setNsid, permission.Lxm, "lxm", Error);
                    CheckAudience(permission, Error);
                    CheckNotApplicable(permission, "rpc", Warn, ("collection", permission.Collection), ("action", permission.Action));
                    break;

                default:
                    Warn($"'{permission.Resource}' is not a resource authorization servers know in a permission set; they ignore the permission");
                    continue;
            }

            foreach (var parameter in permission.OtherParameters?.Keys ?? Enumerable.Empty<string>())
                Warn($"unknown parameter '{parameter}': authorization servers ignore a permission with parameters they do not know");
        }
    }

    /// <summary>
    /// The <c>collection</c> or <c>lxm</c> list: required, no wildcards, and only NSIDs in the set's
    /// own group or below it — never a sibling or parent group.
    /// </summary>
    private static void CheckNsids(Nsid setNsid, List<string>? values, string field, Action<string> error)
    {
        if (values is not { Count: > 0 })
        {
            error($"'{field}' is required and must not be empty");
            return;
        }

        var group = setNsid.Authority + ".";
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (value == "*" || value.EndsWith(".*", StringComparison.Ordinal))
                error($"'{field}' may not use wildcards in a permission set ('{value}')");
            else if (!Nsid.TryParse(value, out _))
                error($"'{field}' entry '{value}' is not an NSID");
            else if (!value.StartsWith(group, StringComparison.Ordinal))
                error($"'{value}' is outside the permission set's namespace '{setNsid.Authority}'");

            if (!seen.Add(value))
                error($"'{field}' lists '{value}' twice");
        }
    }

    private static void CheckActions(List<string>? actions, Action<string> error)
    {
        if (actions is null)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in actions)
        {
            if (!RepoActions.Contains(action))
                error($"'action' '{action}' is not one of create, update, delete");
            if (!seen.Add(action))
                error($"'action' lists '{action}' twice");
        }
    }

    /// <summary>
    /// An <c>rpc</c> audience in a permission set: either <c>*</c>, or none with
    /// <c>inheritAud</c> set — never a service DID, and never both.
    /// </summary>
    private static void CheckAudience(LexiconPermission permission, Action<string> error)
    {
        if (permission.InheritAud == true)
        {
            if (permission.Aud is not null)
                error("'aud' and 'inheritAud' cannot both be set; the permission is invalid");
        }
        else if (permission.Aud is null)
        {
            error("'aud' is required unless 'inheritAud' is true");
        }
        else if (permission.Aud != "*")
        {
            error($"'aud' must be '*' in a permission set, not a service reference ('{permission.Aud}'); use 'inheritAud' to take it from the include scope");
        }
    }

    private static void CheckNotApplicable(
        LexiconPermission permission, string resource, Action<string> warn, params (string Name, object? Value)[] fields)
    {
        foreach (var (field, value) in fields)
        {
            if (value is not null)
                warn($"'{field}' does not apply to '{resource}' permissions; authorization servers ignore a permission with parameters they do not know");
        }
    }

    /// <summary>Reports every use of the removed <c>null</c> type below a definition.</summary>
    private static void FindNullTypes(LexiconSchema def, string name, Action<LintSeverity, string?, string> report)
    {
        var stack = new Stack<(LexiconSchema Schema, string Path)>();
        void Push(LexiconSchema? schema, string path)
        {
            if (schema is not null)
                stack.Push((schema, path));
        }

        Push(def.Record, "record");
        Push(def.Parameters, "parameters");
        Push(def.Input?.Schema, "input");
        Push(def.Output?.Schema, "output");
        Push(def.Message?.Schema, "message");
        Push(def.Items, "items");
        foreach (var (property, schema) in def.Properties ?? [])
            Push(schema, property);

        while (stack.TryPop(out var entry))
        {
            if (entry.Schema.Type == "null")
            {
                report(LintSeverity.Error, name, $"'{entry.Path}': the 'null' type was removed from the Lexicon language");
                continue;
            }

            Push(entry.Schema.Items, entry.Path + "[]");
            foreach (var (property, schema) in entry.Schema.Properties ?? [])
                Push(schema, $"{entry.Path}.{property}");
        }
    }
}
