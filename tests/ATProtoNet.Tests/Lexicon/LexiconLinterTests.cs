using System.Text.Json;
using ATProtoNet.LexiconGenerator.Schema;
using ATProtoNet.LexiconGenerator.Validation;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// <c>atproto-lexgen lint</c>: the document rules, the removed <c>null</c> type, space
/// declarations, and the permission-set rules of https://atproto.com/specs/permission.
/// </summary>
public sealed class LexiconLinterTests
{
    private static LexiconDocument Parse(string json) =>
        JsonSerializer.Deserialize<LexiconDocument>(json, LexiconJson.ReadOptions)!;

    private static IReadOnlyList<LintDiagnostic> Lint(string json) => LexiconLinter.Lint(Parse(json));

    private static IReadOnlyList<LintDiagnostic> LintSet(string permissions, string id = "com.example.feed.authFull", string extra = "") =>
        Lint($$"""
            {
              "lexicon": 1,
              "id": "{{id}}",
              "defs": {
                "main": {
                  "type": "permission-set",
                  "title": "Full access"{{extra}},
                  "permissions": [{{permissions}}]
                }
              }
            }
            """);

    private static void AssertError(IReadOnlyList<LintDiagnostic> diagnostics, string fragment) =>
        Assert.Contains(diagnostics, d => d.Severity == LintSeverity.Error && d.Message.Contains(fragment, StringComparison.Ordinal));

    private static void AssertWarning(IReadOnlyList<LintDiagnostic> diagnostics, string fragment) =>
        Assert.Contains(diagnostics, d => d.Severity == LintSeverity.Warning && d.Message.Contains(fragment, StringComparison.Ordinal));

    // ── Permission sets ──────────────────────────────────────

    [Fact]
    public void Lint_SpecExamplePermissionSet_IsClean()
    {
        // The permission specification's example set (with its NSIDs moved under the set's
        // namespace, which the example itself does not do).
        var diagnostics = LintSet(
            """
            { "type": "permission", "resource": "repo", "collection": ["com.example.feed.post"] },
            { "type": "permission", "resource": "repo", "collection": ["com.example.feed.like"], "action": ["delete"] },
            { "type": "permission", "resource": "rpc", "inheritAud": true, "lxm": ["com.example.feed.getFeed", "com.example.feed.sub.getProfile"] },
            { "type": "permission", "resource": "rpc", "aud": "*", "lxm": ["com.example.feed.getFeedSkeleton"] }
            """,
            extra: """, "title:lang": { "ja": "基本的なアプリ機能" }, "detail": "Creation of posts" """);

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("blob")]
    [InlineData("account")]
    [InlineData("identity")]
    public void Lint_ResourceNotAllowedInASet_IsAnError(string resource)
    {
        var diagnostics = LintSet($$"""{ "type": "permission", "resource": "{{resource}}", "accept": ["image/*"] }""");

        AssertError(diagnostics, $"'{resource}' permissions cannot be part of a permission set");
    }

    [Theory]
    [InlineData("repo", "collection", "*")]
    [InlineData("rpc", "lxm", "*")]
    [InlineData("repo", "collection", "com.example.feed.*")]
    public void Lint_Wildcard_IsAnError(string resource, string field, string value)
    {
        var aud = resource == "rpc" ? """, "aud": "*" """ : "";
        var diagnostics = LintSet($$"""{ "type": "permission", "resource": "{{resource}}", "{{field}}": ["{{value}}"]{{aud}} }""");

        AssertError(diagnostics, "may not use wildcards");
    }

    [Theory]
    // A sibling group, a parent group, another domain.
    [InlineData("com.example.actor.profile")]
    [InlineData("com.example.post")]
    [InlineData("org.other.feed.post")]
    // Shares the group's text but is not below it.
    [InlineData("com.example.feedx.post")]
    public void Lint_NsidOutsideTheSetsNamespace_IsAnError(string collection)
    {
        var diagnostics = LintSet($$"""{ "type": "permission", "resource": "repo", "collection": ["{{collection}}"] }""");

        AssertError(diagnostics, "outside the permission set's namespace 'com.example.feed'");
    }

    [Theory]
    [InlineData("com.example.feed.post")]
    [InlineData("com.example.feed.deep.er.post")]
    public void Lint_NsidInTheSetsGroupOrBelow_IsAllowed(string collection)
    {
        Assert.Empty(LintSet($$"""{ "type": "permission", "resource": "repo", "collection": ["{{collection}}"] }"""));
    }

    [Fact]
    public void Lint_RpcAudAndInheritAudTogether_IsAnError()
    {
        var diagnostics = LintSet("""{ "type": "permission", "resource": "rpc", "aud": "*", "inheritAud": true, "lxm": ["com.example.feed.get"] }""");

        AssertError(diagnostics, "cannot both be set");
    }

    [Fact]
    public void Lint_RpcWithoutAudOrInheritAud_IsAnError()
    {
        var diagnostics = LintSet("""{ "type": "permission", "resource": "rpc", "lxm": ["com.example.feed.get"] }""");

        AssertError(diagnostics, "'aud' is required unless 'inheritAud' is true");
    }

    [Fact]
    public void Lint_RpcWithAServiceDid_IsAnError()
    {
        var diagnostics = LintSet("""{ "type": "permission", "resource": "rpc", "aud": "did:web:api.example.com#svc", "lxm": ["com.example.feed.get"] }""");

        AssertError(diagnostics, "'aud' must be '*' in a permission set");
    }

    [Fact]
    public void Lint_RepoActions_MustBeKnownAndUnique()
    {
        var diagnostics = LintSet("""{ "type": "permission", "resource": "repo", "collection": ["com.example.feed.post"], "action": ["create", "Delete", "create"] }""");

        AssertError(diagnostics, "'Delete' is not one of create, update, delete");
        AssertError(diagnostics, "lists 'create' twice");
    }

    [Theory]
    [InlineData("""{ "type": "permission", "resource": "repo" }""", "'collection' is required")]
    [InlineData("""{ "type": "permission", "resource": "repo", "collection": [] }""", "'collection' is required")]
    [InlineData("""{ "type": "permission", "resource": "rpc", "aud": "*" }""", "'lxm' is required")]
    [InlineData("""{ "type": "permission", "resource": "repo", "collection": ["not an nsid"] }""", "is not an NSID")]
    [InlineData("""{ "type": "permission" }""", "'resource' is required")]
    [InlineData("""{ "type": "scope", "resource": "repo", "collection": ["com.example.feed.post"] }""", "'type' must be 'permission'")]
    public void Lint_MalformedPermission_IsAnError(string permission, string fragment)
    {
        AssertError(LintSet(permission), fragment);
    }

    [Fact]
    public void Lint_UnknownResourceOrParameter_WarnsThatServersIgnoreIt()
    {
        var diagnostics = LintSet(
            """
            { "type": "permission", "resource": "space", "spaceType": "com.example.feed.space" },
            { "type": "permission", "resource": "repo", "collection": ["com.example.feed.post"], "limit": 3 },
            { "type": "permission", "resource": "repo", "collection": ["com.example.feed.post"], "lxm": ["com.example.feed.get"] }
            """);

        Assert.DoesNotContain(diagnostics, d => d.Severity == LintSeverity.Error);
        AssertWarning(diagnostics, "'space' is not a resource authorization servers know");
        AssertWarning(diagnostics, "unknown parameter 'limit'");
        AssertWarning(diagnostics, "'lxm' does not apply to 'repo' permissions");
    }

    [Fact]
    public void Lint_SetWithoutPermissionsOrTitle_IsReported()
    {
        AssertError(Lint("""
            { "lexicon": 1, "id": "com.example.authNone", "defs": { "main": { "type": "permission-set" } } }
            """), "'permissions' is required");

        var empty = Lint("""
            { "lexicon": 1, "id": "com.example.authNone", "defs": { "main": { "type": "permission-set", "permissions": [] } } }
            """);
        AssertWarning(empty, "grants nothing");
        AssertWarning(empty, "no 'title'");
    }

    [Fact]
    public void Lint_PermissionSetNotMain_IsAnError()
    {
        var diagnostics = Lint("""
            {
              "lexicon": 1,
              "id": "com.example.defs",
              "defs": { "authFull": { "type": "permission-set", "title": "x", "permissions": [] } }
            }
            """);

        var error = Assert.Single(diagnostics, d => d.Severity == LintSeverity.Error);
        Assert.Equal("com.example.defs#authFull: a 'permission-set' definition must be named 'main'", error.ToString());
    }

    // ── Documents, null, spaces ──────────────────────────────

    [Fact]
    public void Lint_NullType_IsAnErrorWhereverItIs()
    {
        var diagnostics = Lint("""
            {
              "lexicon": 1,
              "id": "com.example.thing",
              "defs": {
                "empty": { "type": "null" },
                "main": {
                  "type": "record",
                  "key": "tid",
                  "record": { "type": "object", "properties": { "gone": { "type": "null" }, "list": { "type": "array", "items": { "type": "null" } } } }
                }
              }
            }
            """);

        Assert.Equal(3, diagnostics.Count(d => d.Severity == LintSeverity.Error && d.Message.Contains("'null' type was removed")));
        Assert.Contains(diagnostics, d => d.Message.Contains("'record.gone'"));
        Assert.Contains(diagnostics, d => d.Message.Contains("'record.list[]'"));
    }

    [Fact]
    public void Lint_InvalidIdOrNoDefinitions_IsAnError()
    {
        var diagnostics = Lint("""{ "lexicon": 1, "id": "not-an-nsid", "defs": {} }""");

        AssertError(diagnostics, "'id' is not a valid NSID");
        AssertError(diagnostics, "no definitions");
    }

    [Fact]
    public void Lint_UnknownDefinitionType_IsAWarning()
    {
        AssertWarning(
            Lint("""{ "lexicon": 1, "id": "com.example.thing", "defs": { "main": { "type": "permissionSet" } } }"""),
            "'permissionSet' is not a Lexicon definition type");
    }

    [Fact]
    public void Lint_SpaceCollections_WarnsOnWildcardAndNonNsids_NotOnLongNames()
    {
        var diagnostics = Lint($$"""
            {
              "lexicon": 1,
              "id": "com.example.forum",
              "defs": {
                "main": {
                  "type": "space",
                  "key": "any",
                  "name": "{{new string('n', 200)}}",
                  "collections": ["com.example.thread", "*", "nope"]
                }
              }
            }
            """);

        Assert.Equal(2, diagnostics.Count);
        AssertWarning(diagnostics, "must not contain '*'");
        AssertWarning(diagnostics, "'nope' is not an NSID");
    }
}
