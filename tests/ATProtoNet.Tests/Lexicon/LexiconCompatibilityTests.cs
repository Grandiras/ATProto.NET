using ATProtoNet.Lexicon;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>The compatibility rules (LEX-14–LEX-17); 0.8's differ scenarios, adjusted to the spec.</summary>
public sealed class LexiconCompatibilityTests
{
    private const string Id = "com.example.post";
    private const string F = "/defs/main/record/properties/f";
    private const string Props = "/defs/main/record/properties";
    private const string Main = "/defs/main";

    private static IReadOnlyList<LexiconChange> Compare(string baseline, string current) =>
        LexiconCompatibility.Compare(Lex.Catalog(baseline), Lex.Catalog(current)).Changes;

    private static string Field(string schema) => Lex.Record(Id, $"'f':{schema}");

    private static string Text(string constraints) => $"{{'type':'string'{constraints}}}";

    private static string Query(string body, string type = "query") => Lex.Defs("com.example.get", $"'main':{{'type':'{type}',{body}}}");

    private static string Params(string properties, string required = "") =>
        $"'parameters':{{'type':'params','required':[{Lex.Names(required)}],'properties':{{{properties}}}}}";

    private static string Space(string key = "any", string name = "Forum", string collections = "'com.example.thread'", string extra = "") =>
        Lex.Defs("com.example.forum", $"'main':{{'type':'space','key':'{key}','name':'{name}','collections':[{collections}]{extra}}}");

    private static string Set(string title = "Posts", string permissions = "{'type':'permission','resource':'repo','collection':['com.example.feed.post']}") =>
        Lex.Defs("com.example.feed.auth", $"'main':{{'type':'permission-set','title':'{title}','permissions':[{permissions}]}}");

    private const string Output = "'output':{'encoding':'application/json','schema':{'type':'object','properties':{'a':{'type':'string'},'b':{'type':'string'}}}}";

    public static TheoryData<string, string, string, LexiconChangeKind, bool, string> Constraints() => new()
    {
        { "maxLength tightened", Field(Text(",'maxLength':10")), Field(Text(",'maxLength':5")), LexiconChangeKind.Changed, true, F + "/maxLength" },
        { "maxLength loosened", Field(Text(",'maxLength':10")), Field(Text(",'maxLength':20")), LexiconChangeKind.Changed, true, F + "/maxLength" },
        { "maxLength added", Field(Text("")), Field(Text(",'maxLength':10")), LexiconChangeKind.Added, false, F + "/maxLength" },
        { "maxLength removed", Field(Text(",'maxLength':10")), Field(Text("")), LexiconChangeKind.Removed, true, F + "/maxLength" },
        { "minLength tightened", Field(Text(",'minLength':1")), Field(Text(",'minLength':3")), LexiconChangeKind.Changed, true, F + "/minLength" },
        { "minGraphemes loosened", Field(Text(",'minGraphemes':3")), Field(Text(",'minGraphemes':1")), LexiconChangeKind.Changed, true, F + "/minGraphemes" },
        { "maxGraphemes added", Field(Text("")), Field(Text(",'maxGraphemes':5")), LexiconChangeKind.Added, false, F + "/maxGraphemes" },
        { "format changed", Field(Text(",'format':'datetime'")), Field(Text(",'format':'uri'")), LexiconChangeKind.Changed, true, F + "/format" },
        { "format added", Field(Text("")), Field(Text(",'format':'datetime'")), LexiconChangeKind.Added, false, F + "/format" },
        { "format removed", Field(Text(",'format':'datetime'")), Field(Text("")), LexiconChangeKind.Removed, true, F + "/format" },
        { "const changed", Field(Text(",'const':'a'")), Field(Text(",'const':'b'")), LexiconChangeKind.Changed, true, F + "/const" },
        { "default added", Field(Text("")), Field(Text(",'default':'a'")), LexiconChangeKind.Added, false, F + "/default" },
        { "integer minimum loosened", Field("{'type':'integer','minimum':5}"), Field("{'type':'integer','minimum':1}"), LexiconChangeKind.Changed, true, F + "/minimum" },
        { "integer maximum tightened", Field("{'type':'integer','maximum':5}"), Field("{'type':'integer','maximum':1}"), LexiconChangeKind.Changed, true, F + "/maximum" },
        { "boolean default changed", Field("{'type':'boolean','default':true}"), Field("{'type':'boolean','default':false}"), LexiconChangeKind.Changed, true, F + "/default" },
        { "array maxLength tightened", Field("{'type':'array','items':{'type':'string'},'maxLength':9}"), Field("{'type':'array','items':{'type':'string'},'maxLength':3}"), LexiconChangeKind.Changed, true, F + "/maxLength" },
        { "array minLength added", Field("{'type':'array','items':{'type':'string'}}"), Field("{'type':'array','items':{'type':'string'},'minLength':1}"), LexiconChangeKind.Added, false, F + "/minLength" },
        { "bytes maxLength tightened", Field("{'type':'bytes','maxLength':9}"), Field("{'type':'bytes','maxLength':3}"), LexiconChangeKind.Changed, true, F + "/maxLength" },
        { "blob maxSize loosened", Field("{'type':'blob','maxSize':100}"), Field("{'type':'blob','maxSize':200}"), LexiconChangeKind.Changed, true, F + "/maxSize" },
        { "blob accept added", Field("{'type':'blob','accept':['image/png']}"), Field("{'type':'blob','accept':['image/png','image/jpeg']}"), LexiconChangeKind.Added, true, F + "/accept/1" },
        { "blob accept removed", Field("{'type':'blob','accept':['image/png','image/jpeg']}"), Field("{'type':'blob','accept':['image/png']}"), LexiconChangeKind.Removed, true, F + "/accept/1" },
        { "enum value added", Field(Text(",'enum':['a']")), Field(Text(",'enum':['a','b']")), LexiconChangeKind.Added, true, F + "/enum/1" },
        { "enum value removed", Field(Text(",'enum':['a','b']")), Field(Text(",'enum':['a']")), LexiconChangeKind.Removed, true, F + "/enum/1" },
        { "enum introduced", Field(Text("")), Field(Text(",'enum':['a']")), LexiconChangeKind.Added, false, F + "/enum" },
        { "enum dropped", Field(Text(",'enum':['a']")), Field(Text("")), LexiconChangeKind.Removed, true, F + "/enum" },
        { "integer enum value removed", Field("{'type':'integer','enum':[1,2]}"), Field("{'type':'integer','enum':[1]}"), LexiconChangeKind.Removed, true, F + "/enum/1" },
        { "knownValues introduced", Field(Text("")), Field(Text(",'knownValues':['a']")), LexiconChangeKind.Added, false, F + "/knownValues" },
        { "knownValues value added", Field(Text(",'knownValues':['a']")), Field(Text(",'knownValues':['a','b']")), LexiconChangeKind.Added, false, F + "/knownValues/1" },
        { "knownValues value removed", Field(Text(",'knownValues':['a','b']")), Field(Text(",'knownValues':['a']")), LexiconChangeKind.Removed, false, F + "/knownValues/1" },
        { "description added", Field(Text("")), Field(Text(",'description':'x'")), LexiconChangeKind.Added, false, F + "/description" },
        { "description changed", Field(Text(",'description':'x'")), Field(Text(",'description':'y'")), LexiconChangeKind.Changed, false, F + "/description" },
        { "description removed", Field(Text(",'description':'x'")), Field(Text("")), LexiconChangeKind.Removed, false, F + "/description" },
    };

    public static TheoryData<string, string, string, LexiconChangeKind, bool, string> Structure() => new()
    {
        { "type changed", Field("{'type':'string'}"), Field("{'type':'integer'}"), LexiconChangeKind.Changed, true, F },
        { "array items type changed", Field("{'type':'array','items':{'type':'string'}}"), Field("{'type':'array','items':{'type':'integer'}}"), LexiconChangeKind.Changed, true, F + "/items" },
        { "ref target changed", Field("{'type':'ref','ref':'com.example.a'}"), Field("{'type':'ref','ref':'com.example.b'}"), LexiconChangeKind.Changed, true, F + "/ref" },
        { "union member added to an open union", Field("{'type':'union','refs':['com.example.a']}"), Field("{'type':'union','refs':['com.example.a','com.example.b']}"), LexiconChangeKind.Added, false, F + "/refs/1" },
        { "union member added to a closed union", Field("{'type':'union','closed':true,'refs':['com.example.a']}"), Field("{'type':'union','closed':true,'refs':['com.example.a','com.example.b']}"), LexiconChangeKind.Added, true, F + "/refs/1" },
        { "union member removed", Field("{'type':'union','refs':['com.example.a','com.example.b']}"), Field("{'type':'union','refs':['com.example.a']}"), LexiconChangeKind.Removed, true, F + "/refs/1" },
        { "union closed", Field("{'type':'union','refs':['com.example.a']}"), Field("{'type':'union','closed':true,'refs':['com.example.a']}"), LexiconChangeKind.Changed, true, F + "/closed" },
        { "optional property added", Lex.Record(Id, "'a':{'type':'string'}"), Lex.Record(Id, "'a':{'type':'string'},'b':{'type':'string'}"), LexiconChangeKind.Added, false, Props + "/b" },
        { "required property added", Lex.Record(Id, "'a':{'type':'string'}", "a"), Lex.Record(Id, "'a':{'type':'string'},'b':{'type':'string'}", "a,b"), LexiconChangeKind.Added, true, Props + "/b" },
        { "property removed", Lex.Record(Id, "'a':{'type':'string'},'b':{'type':'string'}"), Lex.Record(Id, "'a':{'type':'string'}"), LexiconChangeKind.Removed, true, Props + "/b" },
        { "property became required", Lex.Record(Id, "'a':{'type':'string'}"), Lex.Record(Id, "'a':{'type':'string'}", "a"), LexiconChangeKind.Changed, true, Props + "/a" },
        { "property became optional", Lex.Record(Id, "'a':{'type':'string'}", "a"), Lex.Record(Id, "'a':{'type':'string'}"), LexiconChangeKind.Changed, true, Props + "/a" },
        { "property became nullable", Lex.Record(Id, "'a':{'type':'string'}", "a"), Lex.Record(Id, "'a':{'type':'string'}", "a", nullable: "a"), LexiconChangeKind.Changed, true, Props + "/a" },
        { "nested object property removed", Lex.Record(Id, "'o':{'type':'object','properties':{'x':{'type':'string'}}}"), Lex.Record(Id, "'o':{'type':'object','properties':{}}"), LexiconChangeKind.Removed, true, Props + "/o/properties/x" },
        { "record key changed", Lex.Defs(Id, "'main':{'type':'record','key':'tid','record':{'type':'object','properties':{}}}"), Lex.Defs(Id, "'main':{'type':'record','key':'any','record':{'type':'object','properties':{}}}"), LexiconChangeKind.Changed, true, Main + "/key" },
        { "record key literal changed", Lex.Defs(Id, "'main':{'type':'record','key':'literal:self','record':{'type':'object','properties':{}}}"), Lex.Defs(Id, "'main':{'type':'record','key':'literal:me','record':{'type':'object','properties':{}}}"), LexiconChangeKind.Changed, true, Main + "/key" },
        { "definition added", Lex.Defs(Id, "'main':{'type':'token'}"), Lex.Defs(Id, "'main':{'type':'token'},'extra':{'type':'object','properties':{}}"), LexiconChangeKind.Added, false, "/defs/extra" },
        { "definition removed", Lex.Defs(Id, "'main':{'type':'token'},'extra':{'type':'token'}"), Lex.Defs(Id, "'main':{'type':'token'}"), LexiconChangeKind.Removed, true, "/defs/extra" },
        { "main type changed", Lex.Defs(Id, "'main':{'type':'token'}"), Lex.Defs(Id, "'main':{'type':'object','properties':{}}"), LexiconChangeKind.Changed, true, Main },
    };

    public static TheoryData<string, string, string, LexiconChangeKind, bool, string> Methods() => new()
    {
        { "optional parameter added", Query(Params("'a':{'type':'string'}")), Query(Params("'a':{'type':'string'},'b':{'type':'string'}")), LexiconChangeKind.Added, false, Main + "/parameters/properties/b" },
        { "required parameter added", Query(Params("'a':{'type':'string'}")), Query(Params("'a':{'type':'string'},'b':{'type':'string'}", "b")), LexiconChangeKind.Added, true, Main + "/parameters/properties/b" },
        { "parameter type changed", Query(Params("'a':{'type':'string'}")), Query(Params("'a':{'type':'integer'}")), LexiconChangeKind.Changed, true, Main + "/parameters/properties/a" },
        { "parameters introduced", Query("'output':{'encoding':'application/json'}"), Query(Params("'a':{'type':'string'}") + ",'output':{'encoding':'application/json'}"), LexiconChangeKind.Added, false, Main + "/parameters/properties/a" },
        { "parameters dropped", Query(Params("'a':{'type':'string'}") + ",'output':{'encoding':'application/json'}"), Query("'output':{'encoding':'application/json'}"), LexiconChangeKind.Removed, true, Main + "/parameters/properties/a" },
        { "output encoding changed", Query("'output':{'encoding':'application/json'}"), Query("'output':{'encoding':'text/plain'}"), LexiconChangeKind.Changed, true, Main + "/output/encoding" },
        { "output property removed", Query(Output), Query("'output':{'encoding':'application/json','schema':{'type':'object','properties':{'a':{'type':'string'}}}}"), LexiconChangeKind.Removed, true, Main + "/output/schema/properties/b" },
        { "output added", Query(Params("'a':{'type':'string'}")), Query(Params("'a':{'type':'string'}") + ",'output':{'encoding':'application/json'}"), LexiconChangeKind.Added, false, Main + "/output" },
        { "output removed", Query(Params("'a':{'type':'string'}") + ",'output':{'encoding':'application/json'}"), Query(Params("'a':{'type':'string'}")), LexiconChangeKind.Removed, true, Main + "/output" },
        { "procedure input added", Query("'output':{'encoding':'application/json'}", "procedure"), Query("'input':{'encoding':'application/json'},'output':{'encoding':'application/json'}", "procedure"), LexiconChangeKind.Added, true, Main + "/input" },
        { "procedure input schema added", Query("'input':{'encoding':'application/json'}", "procedure"), Query("'input':{'encoding':'application/json','schema':{'type':'object','properties':{}}}", "procedure"), LexiconChangeKind.Added, true, Main + "/input/schema" },
        { "error declared", Query("'errors':[]"), Query("'errors':[{'name':'Gone'}]"), LexiconChangeKind.Added, false, Main + "/errors/0" },
        { "error no longer declared", Query("'errors':[{'name':'Gone'}]"), Query("'errors':[]"), LexiconChangeKind.Removed, false, Main + "/errors/0" },
        { "error description changed", Query("'errors':[{'name':'Gone','description':'a'}]"), Query("'errors':[{'name':'Gone','description':'b'}]"), LexiconChangeKind.Changed, false, Main + "/errors/0/description" },
        { "query became procedure", Query("'output':{'encoding':'application/json'}"), Query("'output':{'encoding':'application/json'}", "procedure"), LexiconChangeKind.Changed, true, Main },
        {
            "subscription message union member added",
            Query("'message':{'schema':{'type':'union','refs':['com.example.a']}}", "subscription"),
            Query("'message':{'schema':{'type':'union','refs':['com.example.a','com.example.b']}}", "subscription"), LexiconChangeKind.Added, false, Main + "/message/schema/refs/1"
        },
    };

    // The default collection set is read when a grant is evaluated, so a new collection widens grants already consented to
    // (compatible, but reported); a removed one narrows them.
    public static TheoryData<string, string, string, LexiconChangeKind, bool, string> Spaces() => new()
    {
        { "space key changed", Space(), Space(key: "tid"), LexiconChangeKind.Changed, true, Main + "/key" },
        { "space name changed", Space(), Space(name: "Forums"), LexiconChangeKind.Changed, false, Main + "/name" },
        { "space localized name added", Space(), Space(extra: ",'name:lang':{'es':'Foro'}"), LexiconChangeKind.Added, false, Main + "/name:lang/es" },
        { "space localized name changed", Space(extra: ",'name:lang':{'es':'Foro'}"), Space(extra: ",'name:lang':{'es':'Foros'}"), LexiconChangeKind.Changed, false, Main + "/name:lang/es" },
        { "space localized name removed", Space(extra: ",'name:lang':{'es':'Foro'}"), Space(), LexiconChangeKind.Removed, false, Main + "/name:lang/es" },
        { "space collection added", Space(), Space(collections: "'com.example.thread','com.example.reply'"), LexiconChangeKind.Added, false, Main + "/collections/1" },
        { "space collection removed", Space(collections: "'com.example.thread','com.example.reply'"), Space(), LexiconChangeKind.Removed, true, Main + "/collections/1" },
        { "space replaced by a record", Space(), Lex.Record("com.example.forum", "'a':{'type':'string'}"), LexiconChangeKind.Changed, true, Main },
        { "permission set title changed", Set(), Set(title: "Everything"), LexiconChangeKind.Changed, false, Main + "/title" },
        {
            "permission added", Set(),
            Set(permissions: "{'type':'permission','resource':'repo','collection':['com.example.feed.post']},{'type':'permission','resource':'repo','collection':['com.example.feed.like']}"),
            LexiconChangeKind.Added, false, Main + "/permissions/1"
        },
        { "permission removed", Set(), Set(permissions: ""), LexiconChangeKind.Removed, true, Main + "/permissions/0" },
    };

    [Theory]
    [MemberData(nameof(Constraints))]
    [MemberData(nameof(Structure))]
    [MemberData(nameof(Methods))]
    [MemberData(nameof(Spaces))]
    [Trait("Spec", "LEX-14")]
    [Trait("Spec", "LEX-15")]
    [Trait("Spec", "LEX-16")]
    public void Compare_OneChange_ReportsKindClassificationAndPointer(string name, string baseline, string current, LexiconChangeKind kind, bool breaking, string pointer)
    {
        _ = name;

        var change = Assert.Single(Compare(baseline, current));

        Assert.Equal(kind, change.Kind);
        Assert.Equal(breaking, change.IsBreaking);
        Assert.Equal(pointer, change.Pointer);
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void Compare_IdenticalCatalogs_ReportNothing()
    {
        var catalog = Lex.Catalog(Space(), Set(), Query(Output), Lex.Record(Id, "'a':{'type':'string'}", "a"));

        var report = LexiconCompatibility.Compare(catalog, catalog);

        Assert.Empty(report.Changes);
        Assert.Empty(report.SuggestedRevisions);
        Assert.False(report.HasBreakingChanges);
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void Compare_SchemaAddedOrRemoved_ReportsAtTheRoot()
    {
        var post = Lex.Record(Id, "'a':{'type':'string'}");
        var like = Lex.Record("com.example.like", "'a':{'type':'string'}");

        var added = LexiconCompatibility.Compare(Lex.Catalog(post), Lex.Catalog(post, like));
        var removed = LexiconCompatibility.Compare(Lex.Catalog(post, like), Lex.Catalog(post));

        var add = Assert.Single(added.Changes);
        Assert.Equal(("com.example.like", "", LexiconChangeKind.Added, false), (add.Nsid.Value, add.Pointer, add.Kind, add.IsBreaking));
        var remove = Assert.Single(removed.Changes);
        Assert.Equal(("com.example.like", "", LexiconChangeKind.Removed, true), (remove.Nsid.Value, remove.Pointer, remove.Kind, remove.IsBreaking));
        Assert.True(removed.HasBreakingChanges);
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void Compare_SameReferenceSpelledDifferently_IsNoChange()
    {
        var baseline = Lex.Defs(Id, "'main':{'type':'record','key':'tid','record':{'type':'object','properties':{'a':{'type':'ref','ref':'#other'},'u':{'type':'union','refs':['#main']}}}},'other':{'type':'object','properties':{}}");
        var current = baseline.Replace("'#other'", "'com.example.post#other'").Replace("'#main'", "'com.example.post'");

        Assert.Empty(Compare(baseline, current));
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void Compare_EnumReordered_IsNoChange()
    {
        Assert.Empty(Compare(Field(Text(",'enum':['a','b']")), Field(Text(",'enum':['b','a']"))));
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void Compare_SeveralChanges_ReportsEachSortedByNsid()
    {
        var baseline = Lex.Catalog(Lex.Record("com.example.b", "'a':{'type':'string'}"), Lex.Record("com.example.a", "'a':{'type':'string'}"));
        var current = Lex.Catalog(Lex.Record("com.example.b", "'a':{'type':'string'},'x':{'type':'string'}"), Lex.Record("com.example.a", "'a':{'type':'integer'}"));

        var changes = LexiconCompatibility.Compare(baseline, current).Changes;

        Assert.Equal(["com.example.a", "com.example.b"], changes.Select(c => c.Nsid.Value));
        Assert.Equal([true, false], changes.Select(c => c.IsBreaking));
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void Compare_TwoChangesInOneSchema_ReportsBoth()
    {
        var changes = Compare(Lex.Record(Id, "'a':{'type':'string'},'b':{'type':'string'}"), Lex.Record(Id, "'a':{'type':'integer'},'c':{'type':'string'}"));

        Assert.Equal([Props + "/a", Props + "/b", Props + "/c"], changes.Select(c => c.Pointer).Order(StringComparer.Ordinal));
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void ToString_Change_NamesSchemaPointerAndClassification()
    {
        var change = Assert.Single(Compare(Field(Text(",'maxLength':10")), Field(Text(",'maxLength':5"))));

        Assert.StartsWith($"{Id} {F}/maxLength: breaking: ", change.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, 2)]
    [InlineData(1, 2)]
    [InlineData(2, 3)]
    [Trait("Spec", "LEX-17")]
    public void Compare_CompatibleChangesOnly_SuggestsTheNextRevision(int? revision, int expected)
    {
        var rev = revision is null ? "" : $",'revision':{revision}";

        var report = LexiconCompatibility.Compare(
            Lex.Catalog(Lex.Record(Id, "'a':{'type':'string'}", extra: rev)), Lex.Catalog(Lex.Record(Id, "'a':{'type':'string'},'b':{'type':'string'}", extra: rev)));

        Assert.Equal(expected, report.SuggestedRevisions[Nsid.Parse(Id)]);
    }

    [Fact]
    [Trait("Spec", "LEX-17")]
    public void Compare_BreakingAndCompatibleChangesTogether_SuggestsNoRevision()
    {
        var report = LexiconCompatibility.Compare(
            Lex.Catalog(Lex.Record(Id, "'a':{'type':'string'}")), Lex.Catalog(Lex.Record(Id, "'a':{'type':'integer'},'b':{'type':'string'}")));

        Assert.Empty(report.SuggestedRevisions);
    }

    [Fact]
    [Trait("Spec", "LEX-17")]
    public void Compare_RevisionAlreadyBumped_SuggestsNothingMore()
    {
        var report = LexiconCompatibility.Compare(
            Lex.Catalog(Lex.Record(Id, "'a':{'type':'string'}", extra: ",'revision':1")),
            Lex.Catalog(Lex.Record(Id, "'a':{'type':'string'},'b':{'type':'string'}", extra: ",'revision':2")));

        Assert.Empty(report.SuggestedRevisions);
    }

    [Fact]
    [Trait("Spec", "LEX-17")]
    public void Compare_OnlySomeSchemasChanged_SuggestsOnlyForThose()
    {
        var like = Lex.Record("com.example.like", "'a':{'type':'string'}");
        var report = LexiconCompatibility.Compare(
            Lex.Catalog(Lex.Record(Id, "'a':{'type':'string'}"), like), Lex.Catalog(Lex.Record(Id, "'a':{'type':'string'},'b':{'type':'string'}"), like, Lex.Record("com.example.new", "")));

        Assert.Equal([Nsid.Parse(Id)], report.SuggestedRevisions.Keys);
    }

    [Fact]
    [Trait("Spec", "LEX-14")]
    public void Compare_NullCatalog_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => LexiconCompatibility.Compare(null!, Lex.Catalog()));
        Assert.Throws<ArgumentNullException>(() => LexiconCompatibility.Compare(Lex.Catalog(), null!));
    }
}
