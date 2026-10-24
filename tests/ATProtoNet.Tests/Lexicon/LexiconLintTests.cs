using ATProtoNet.Lexicon;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>The lint rules: permission sets (LEX-11), space collections (LEX-12) and style (LEX-13); 0.8's linter scenarios on rule IDs.</summary>
public sealed class LexiconLintTests
{
    private const string Set = "/defs/main";

    private static IReadOnlyList<LexiconDiagnostic> Lint(params string[] documents) => LexiconLint.Run(Lex.Catalog(documents));

    private static IReadOnlyList<LexiconDiagnostic> LintSet(string permissions, string id = "com.example.feed.authFull", string extra = "") =>
        Lint(Lex.Defs(id, $"'main':{{'type':'permission-set','description':'Everything','title':'Full access'{extra},'permissions':[{permissions}]}}"));

    private static IReadOnlyList<LexiconDiagnostic> LintDef(string def, string id = "com.example.thing") => Lint(Lex.Defs(id, $"'main':{def}"));

    private static string Repo(string collection = "'com.example.feed.post'", string extra = "") =>
        $"{{'type':'permission','resource':'repo','collection':[{collection}]{extra}}}";

    private static string Rpc(string lxm = "'com.example.feed.get'", string audience = ",'aud':'*'") =>
        $"{{'type':'permission','resource':'rpc','lxm':[{lxm}]{audience}}}";

    private static void AssertFinding(IReadOnlyList<LexiconDiagnostic> diagnostics, string rule, LexiconSeverity severity, string pointer) =>
        Assert.Contains(diagnostics, d => d.RuleId == rule && d.Severity == severity && d.Pointer == pointer);

    [Fact]
    [Trait("Spec", "LEX-11")]
    public void Run_SpecExamplePermissionSet_IsClean()
    {
        // The permission specification's example set, with its NSIDs moved under the set's own namespace.
        var diagnostics = LintSet(
            $$"""
            {{Repo()}},
            {{Repo("'com.example.feed.like'", ",'action':['delete']")}},
            {'type':'permission','resource':'rpc','inheritAud':true,'lxm':['com.example.feed.getFeed','com.example.feed.sub.getProfile']},
            {{Rpc("'com.example.feed.getFeedSkeleton'")}}
            """,
            extra: ",'title:lang':{'ja':'基本的なアプリ機能'},'detail':'Creation of posts'");

        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("blob")]
    [InlineData("account")]
    [InlineData("identity")]
    [Trait("Spec", "LEX-11")]
    public void Run_ResourceNotAllowedInASet_IsAnError(string resource)
    {
        var diagnostics = LintSet($"{{'type':'permission','resource':'{resource}','accept':['image/*']}}");

        AssertFinding(diagnostics, "LXL021", LexiconSeverity.Error, Set + "/permissions/0");
        Assert.Contains("cannot be part of a permission set", diagnostics.Single().Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("repo", "*")]
    [InlineData("repo", "com.example.feed.*")]
    [InlineData("rpc", "*")]
    [InlineData("rpc", "com.example.feed.*")]
    [Trait("Spec", "LEX-11")]
    public void Run_Wildcard_IsAnError(string resource, string value)
    {
        var diagnostics = LintSet(resource == "repo" ? Repo($"'{value}'") : Rpc($"'{value}'"));

        var field = resource == "repo" ? "collection" : "lxm";
        AssertFinding(diagnostics, "LXL023", LexiconSeverity.Error, $"{Set}/permissions/0/{field}/0");
        Assert.Contains("wildcards", Assert.Single(diagnostics).Message, StringComparison.Ordinal);
    }

    [Theory]
    // A sibling group, a parent group, another domain, and a name that shares the group's text without being below it.
    [InlineData("com.example.actor.profile")]
    [InlineData("com.example.post")]
    [InlineData("org.other.feed.post")]
    [InlineData("com.example.feedx.post")]
    [Trait("Spec", "LEX-11")]
    public void Run_NsidOutsideTheSetsNamespace_IsAnError(string collection)
    {
        var diagnostics = LintSet(Repo($"'{collection}'"));

        AssertFinding(diagnostics, "LXL023", LexiconSeverity.Error, Set + "/permissions/0/collection/0");
        Assert.Contains("com.example.feed", Assert.Single(diagnostics).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("com.example.feed.post")]
    [InlineData("com.example.feed.deep.er.post")]
    [Trait("Spec", "LEX-11")]
    public void Run_NsidInTheSetsGroupOrBelow_IsAllowed(string collection)
    {
        Assert.Empty(LintSet(Repo($"'{collection}'")));
    }

    [Fact]
    [Trait("Spec", "LEX-11")]
    public void Run_RepoActions_MustBeKnownAndUnique()
    {
        var diagnostics = LintSet(Repo(extra: ",'action':['create','Delete','create']"));

        AssertFinding(diagnostics, "LXL021", LexiconSeverity.Error, Set + "/permissions/0/action/1");
        AssertFinding(diagnostics, "LXL021", LexiconSeverity.Error, Set + "/permissions/0/action/2");
        Assert.Equal(2, diagnostics.Count);
    }

    [Fact]
    [Trait("Spec", "LEX-11")]
    public void Run_DuplicateCollection_IsAnError()
    {
        var diagnostics = LintSet(Repo("'com.example.feed.post','com.example.feed.post'"));

        AssertFinding(diagnostics, "LXL021", LexiconSeverity.Error, Set + "/permissions/0/collection/1");
    }

    [Theory]
    [InlineData("{'type':'permission','resource':'repo'}", "/collection")]
    [InlineData("{'type':'permission','resource':'repo','collection':[]}", "/collection")]
    [InlineData("{'type':'permission','resource':'repo','collection':['not an nsid']}", "/collection/0")]
    [InlineData("{'type':'permission','resource':'repo','collection':[1]}", "/collection/0")]
    [InlineData("{'type':'permission','resource':'repo','collection':['com.example.feed.post'],'action':'create'}", "/action")]
    [InlineData("{'type':'permission','resource':'rpc','aud':'*'}", "/lxm")]
    [InlineData("{'type':'permission'}", "")]
    [InlineData("{'resource':'repo','collection':['com.example.feed.post']}", "")]
    [InlineData("{'type':'scope','resource':'repo','collection':['com.example.feed.post']}", "")]
    [InlineData("{'type':'permission','resource':'rpc','aud':'*','inheritAud':true,'lxm':['com.example.feed.get']}", "/aud")]
    [InlineData("{'type':'permission','resource':'rpc','lxm':['com.example.feed.get']}", "/aud")]
    [InlineData("{'type':'permission','resource':'rpc','aud':'did:web:api.example.com#svc','lxm':['com.example.feed.get']}", "/aud")]
    [InlineData("42", "")]
    [Trait("Spec", "LEX-11")]
    public void Run_MalformedPermission_IsAnError(string permission, string suffix)
    {
        var diagnostics = LintSet(permission);

        AssertFinding(diagnostics, "LXL021", LexiconSeverity.Error, Set + "/permissions/0" + suffix);
    }

    [Fact]
    [Trait("Spec", "LEX-11")]
    public void Run_UnknownResourceOrParameter_WarnsThatServersIgnoreIt()
    {
        var diagnostics = LintSet($"{{'type':'permission','resource':'space','spaceType':'com.example.feed.space'}},{Repo(extra: ",'limit':3")},{Repo(extra: ",'lxm':['com.example.feed.get']")}");

        Assert.DoesNotContain(diagnostics, d => d.Severity == LexiconSeverity.Error);
        AssertFinding(diagnostics, "LXL024", LexiconSeverity.Warning, Set + "/permissions/0/resource");
        AssertFinding(diagnostics, "LXL022", LexiconSeverity.Warning, Set + "/permissions/1/limit");
        AssertFinding(diagnostics, "LXL022", LexiconSeverity.Warning, Set + "/permissions/2/lxm");
    }

    [Fact]
    [Trait("Spec", "LEX-11")]
    public void Run_SetWithoutPermissionsOrTitle_IsReported()
    {
        var diagnostics = Lint(Lex.Defs("com.example.authNone", "'main':{'type':'permission-set','description':'d','permissions':[]}"));

        AssertFinding(diagnostics, "LXL020", LexiconSeverity.Error, Set);
        AssertFinding(diagnostics, "LXL020", LexiconSeverity.Error, Set + "/permissions");
    }

    [Fact]
    [Trait("Spec", "LEX-11")]
    public void Parse_PermissionSetNotMain_IsAnError()
    {
        var e = Assert.Throws<FormatException>(() => Lex.Doc(Lex.Defs("com.example.defs", "'authFull':{'type':'permission-set','title':'x','permissions':[]}")));

        Assert.Contains("LXL002", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "LEX-12")]
    public void Run_SpaceCollections_WarnsOnWildcardAndNonNsids_NotOnLongNames()
    {
        var diagnostics = LintDef($"{{'type':'space','description':'d','key':'any','name':'{new string('n', 200)}','collections':['com.example.thread','*','nope','com.example.*']}}", "com.example.forum");

        Assert.Equal([("LXL030", "/defs/main/collections/1"), ("LXL030", "/defs/main/collections/2"), ("LXL030", "/defs/main/collections/3")], diagnostics.Select(d => (d.RuleId, d.Pointer)));
        Assert.All(diagnostics, d => Assert.Equal(LexiconSeverity.Warning, d.Severity));
    }

    [Fact]
    [Trait("Spec", "LEX-12")]
    public void Run_SpaceWithValidCollections_IsClean()
    {
        Assert.Empty(LintDef("{'type':'space','description':'d','key':'any','name':'Forum','collections':['com.example.thread','com.example.reply']}", "com.example.forum"));
    }

    [Theory]
    [InlineData("good", false)]
    [InlineData("goodName2", false)]
    [InlineData("Upper", true)]
    [InlineData("snake_case", true)]
    [InlineData("kebab-case", true)]
    [InlineData("1st", true)]
    [Trait("Spec", "LEX-13")]
    public void Run_PropertyName_MustBeCamelCase(string name, bool warns)
    {
        var diagnostics = LintDef($"{{'type':'record','key':'tid','description':'d','record':{{'type':'object','properties':{{'{name}':{{'type':'string'}}}}}}}}");

        Assert.Equal(warns, diagnostics.Any(d => d.RuleId == "LXL040" && d.Severity == LexiconSeverity.Warning && d.Pointer == $"/defs/main/record/properties/{name}"));
    }

    [Theory]
    [InlineData("{'type':'record','key':'tid','record':{'type':'object','properties':{}}}")]
    [InlineData("{'type':'query'}")]
    [InlineData("{'type':'procedure'}")]
    [InlineData("{'type':'subscription'}")]
    [InlineData("{'type':'permission-set','title':'t','permissions':[{'type':'permission','resource':'repo','collection':['com.example.thing']}]}")]
    [InlineData("{'type':'space','collections':[]}")]
    [Trait("Spec", "LEX-13")]
    public void Run_PrimaryDefinitionWithoutDescription_IsAWarning(string def)
    {
        AssertFinding(LintDef(def), "LXL041", LexiconSeverity.Warning, "/defs/main");
        Assert.Empty(LintDef(def.Insert(1, "'description':'d',")));
    }

    [Fact]
    [Trait("Spec", "LEX-13")]
    public void Run_DescriptionIsOnlyExpectedOnPrimaryDefinitions()
    {
        Assert.Empty(Lint(Lex.Defs("com.example.defs", "'main':{'type':'object','properties':{}},'tok':{'type':'token'},'s':{'type':'string'}")));
    }

    [Fact]
    [Trait("Spec", "LEX-13")]
    public void Run_MaxGraphemesWithoutMaxLength_IsAWarningWhereverTheStringIs()
    {
        var diagnostics = Lint(Lex.Defs("com.example.thing", """
            'main':{'type':'query','description':'d','output':{'encoding':'application/json','schema':{'type':'object','properties':{
              'bare':{'type':'string','maxGraphemes':5},
              'paired':{'type':'string','maxGraphemes':5,'maxLength':50},
              'list':{'type':'array','items':{'type':'string','maxGraphemes':5}}}}}},
            'rec':{'type':'object','properties':{'t':{'type':'string','maxGraphemes':5}}}
            """));

        Assert.Equal(
            ["/defs/main/output/schema/properties/bare", "/defs/main/output/schema/properties/list/items", "/defs/rec/properties/t"],
            diagnostics.Where(d => d.RuleId == "LXL042").Select(d => d.Pointer));
    }

    [Fact]
    [Trait("Spec", "LEX-10")]
    public void Run_SeveralDocuments_SortsByNsidThenPointer()
    {
        var diagnostics = Lint(
            Lex.Defs("com.example.b", "'main':{'type':'query'},'x':{'type':'object','properties':{'Bad':{'type':'string'}}}"),
            Lex.Defs("com.example.a", "'main':{'type':'query'}"));

        Assert.Equal(
            [("com.example.a", "/defs/main"), ("com.example.b", "/defs/main"), ("com.example.b", "/defs/x/properties/Bad")],
            diagnostics.Select(d => (d.DocumentId!, d.Pointer)));
    }

    [Fact]
    [Trait("Spec", "LEX-10")]
    public void ToString_Diagnostic_NamesDocumentPointerSeverityAndRule()
    {
        var diagnostic = Assert.Single(LintDef("{'type':'query'}"));

        Assert.Equal("com.example.thing /defs/main: warning LXL041: A primary definition has a description.", diagnostic.ToString());
    }
}
