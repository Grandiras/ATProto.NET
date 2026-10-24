using System.Diagnostics.CodeAnalysis;
using ATProtoNet.Lexicon;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Reference resolution and cycle detection in a <see cref="LexiconCatalog"/> (LEX-6–LEX-9).</summary>
public sealed class LexiconCatalogTests
{
    private const string Defs = "com.example.defs";
    private const string Thing = "/defs/main/record/properties/p";

    private static string Doc(string id, string defs) => Lex.Defs(id, defs);

    // A document "com.example.defs" with a main object, a named object and whatever else a test adds.
    private static string Target(string extra = "") =>
        Doc(Defs, "'main':{'type':'object','properties':{}},'thing':{'type':'object','properties':{}}" + extra);

    private static string Prop(string schema) => Lex.Record("com.example.use", $"'p':{schema}");

    private static LexiconCatalog Catalog(params string[] documents) => Lex.Catalog(documents);

    private static (string Rule, string Pointer)[] Findings(LexiconCatalog catalog) => [.. catalog.Diagnostics.Select(d => (d.RuleId, d.Pointer)).Order()];

    [Theory]
    [InlineData("com.example.defs", "main")]
    [InlineData("com.example.defs#thing", "thing")]
    [InlineData("com.example.defs#main", "main")]
    [Trait("Spec", "LEX-6")]
    public void TryResolve_AbsoluteReference_FindsTheDefinition(string reference, string def)
    {
        var catalog = Catalog(Target());

        Assert.True(catalog.TryResolve(reference, Nsid.Parse("com.example.use"), out var resolved));

        Assert.Equal(new LexRefTarget(Nsid.Parse(Defs), def), resolved.Target);
        Assert.Same(catalog.Documents.Single().Defs[def], resolved.Definition);
    }

    [Theory]
    [InlineData("#main", "main")]
    [InlineData("#thing", "thing")]
    [Trait("Spec", "LEX-6")]
    public void TryResolve_LocalReference_ResolvesWithinTheContextDocument(string reference, string def)
    {
        var catalog = Catalog(Target());

        Assert.True(catalog.TryResolve(reference, Nsid.Parse(Defs), out var resolved));

        Assert.Equal(def, resolved.Target.DefName);
        Assert.False(catalog.TryResolve(reference, Nsid.Parse("com.example.elsewhere"), out _));
    }

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void TryResolve_ThreeForms_AreTheSameDefinition()
    {
        var catalog = Catalog(Target());
        var context = Nsid.Parse(Defs);

        catalog.TryResolve("com.example.defs", context, out var bare);
        catalog.TryResolve("com.example.defs#main", context, out var named);
        catalog.TryResolve("#main", context, out var local);

        Assert.Equal(bare, named);
        Assert.Equal(bare, local);
        Assert.Equal("com.example.defs", bare!.Target.ToString());
    }

    [Theory]
    [InlineData("com.example.nothing")]
    [InlineData("com.example.defs#nothing")]
    [InlineData("#nothing")]
    [InlineData("not a reference")]
    [InlineData("com.example.defs#")]
    [Trait("Spec", "LEX-6")]
    public void TryResolve_UnresolvableReference_ReturnsFalse(string reference)
    {
        Assert.False(Catalog(Target()).TryResolve(reference, Nsid.Parse(Defs), out _));
    }

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Create_UnresolvedReferences_AreDiagnosticsWithTheReferringPointer()
    {
        var catalog = Catalog(Doc("com.example.use", """
            'main':{'type':'record','key':'tid','record':{'type':'object','properties':{
              'direct':{'type':'ref','ref':'com.example.missing'},
              'list':{'type':'array','items':{'type':'ref','ref':'#missing'}},
              'one':{'type':'union','refs':['com.example.missing#a','#main']}}}},
            'method':{'type':'object','properties':{}},
            'm2':{'type':'object','properties':{'x':{'type':'ref','ref':'not a reference'}}}
            """));

        Assert.Equal(
            [("LXL010", "/defs/m2/properties/x/ref"), ("LXL010", "/defs/main/record/properties/direct/ref"),
             ("LXL010", "/defs/main/record/properties/list/items/ref"), ("LXL010", "/defs/main/record/properties/one/refs/0")],
            Findings(catalog));
    }

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Create_UnresolvedReferencesInMethods_ArePointedAtInTheirBodies()
    {
        var catalog = Catalog(
            Doc("com.example.q", "'main':{'type':'query','output':{'encoding':'application/json','schema':{'type':'ref','ref':'#nope'}}}"),
            Doc("com.example.p", "'main':{'type':'procedure','input':{'encoding':'application/json','schema':{'type':'ref','ref':'#nope'}}}"),
            Doc("com.example.s", "'main':{'type':'subscription','message':{'schema':{'type':'union','refs':['#nope']}}}"));

        Assert.Equal(
            [("LXL010", "/defs/main/input/schema/ref"), ("LXL010", "/defs/main/message/schema/refs/0"), ("LXL010", "/defs/main/output/schema/ref")],
            Findings(catalog));
    }

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Create_AllReferencesResolve_HasNoDiagnostics()
    {
        var catalog = Catalog(Target(), Lex.Record("com.example.rec", ""), Prop("{'type':'union','refs':['com.example.defs','com.example.defs#thing','com.example.rec']}"));

        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    [Trait("Spec", "LEX-7")]
    public void Create_RefToAToken_IsAnError()
    {
        var catalog = Catalog(Target(",'flag':{'type':'token'}"), Prop("{'type':'ref','ref':'com.example.defs#flag'}"));

        Assert.Equal([("LXL011", Thing + "/ref")], Findings(catalog));
    }

    [Fact]
    [Trait("Spec", "LEX-7")]
    public void Create_UnionMemberThatIsAUnion_IsAnError()
    {
        var catalog = Catalog(Target(",'u':{'type':'union','refs':['#thing']}"), Prop("{'type':'union','refs':['com.example.defs#u','com.example.defs#thing']}"));

        Assert.Contains(("LXL011", Thing + "/refs/0"), Findings(catalog));
        Assert.DoesNotContain(("LXL011", Thing + "/refs/1"), Findings(catalog));
    }

    [Fact]
    [Trait("Spec", "LEX-8")]
    public void TryResolve_ExternalDefinition_ResolvesThroughIExternalLexicons()
    {
        var external = new FakeExternal(new ExternalDefinition(new(Nsid.Parse("app.other.defs"), "view"), "App.Other.View", ExternalDefinitionKind.Object));
        var catalog = LexiconCatalog.Create([Lex.Doc(Prop("{'type':'ref','ref':'app.other.defs#view'}"))], external);

        Assert.Empty(catalog.Diagnostics);
        Assert.True(catalog.TryResolve("app.other.defs#view", Nsid.Parse("com.example.use"), out var resolved));
        Assert.Equal("App.Other.View", resolved.External!.TypeName);
        Assert.Null(resolved.Definition);
        Assert.False(catalog.TryResolve("app.other.defs#nope", Nsid.Parse("com.example.use"), out _));
    }

    [Fact]
    [Trait("Spec", "LEX-8")]
    public void Create_ExternalTokenOrUnion_AreRejectedLikeLocalOnes()
    {
        var external = new FakeExternal(
            new ExternalDefinition(new(Nsid.Parse("app.other.defs"), "flag"), "App.Other.Flag", ExternalDefinitionKind.Token),
            new ExternalDefinition(new(Nsid.Parse("app.other.defs"), "choice"), "App.Other.Choice", ExternalDefinitionKind.Union));
        var catalog = LexiconCatalog.Create(
            [Lex.Doc(Prop("{'type':'ref','ref':'app.other.defs#flag'}")), Lex.Doc(Lex.Record("com.example.use2", "'p':{'type':'union','refs':['app.other.defs#choice']}"))], external);

        Assert.Equal([("LXL011", Thing + "/ref"), ("LXL011", Thing + "/refs/0")], Findings(catalog));
    }

    [Fact]
    [Trait("Spec", "LEX-9")]
    public void Create_RequiredSelfReference_IsAnError()
    {
        var catalog = Catalog(Doc(Defs, "'main':{'type':'object','required':['me'],'properties':{'me':{'type':'ref','ref':'#main'}}}"));

        Assert.Equal("LXL012", Assert.Single(catalog.Diagnostics).RuleId);
    }

    [Fact]
    [Trait("Spec", "LEX-9")]
    public void Create_RequiredMutualReferences_AreAnError()
    {
        var catalog = Catalog(Doc(Defs, """
            'main':{'type':'object','required':['b'],'properties':{'b':{'type':'ref','ref':'#b'}}},
            'b':{'type':'object','required':['a'],'properties':{'a':{'type':'ref','ref':'#main'}}}
            """));

        Assert.Contains(catalog.Diagnostics, d => d.RuleId == "LXL012");
    }

    [Fact]
    [Trait("Spec", "LEX-9")]
    public void Create_RequiredReferenceCycleAcrossDocumentsAndRecords_IsAnError()
    {
        var catalog = Catalog(
            Lex.Record("com.example.a", "'b':{'type':'ref','ref':'com.example.b'}", "b"),
            Lex.Record("com.example.b", "'a':{'type':'ref','ref':'com.example.a'}", "a"));

        Assert.Contains(catalog.Diagnostics, d => d.RuleId == "LXL012");
    }

    [Theory]
    [InlineData("'required':[],'properties':{'me':{'type':'ref','ref':'#main'}}")]
    [InlineData("'required':['me'],'nullable':['me'],'properties':{'me':{'type':'ref','ref':'#main'}}")]
    [InlineData("'required':['me'],'properties':{'me':{'type':'array','items':{'type':'ref','ref':'#main'}}}")]
    [InlineData("'required':['me'],'properties':{'me':{'type':'union','refs':['#main']}}")]
    [Trait("Spec", "LEX-9")]
    public void Create_RecursionThroughOptionalNullableArrayOrUnion_IsAllowed(string body)
    {
        var catalog = Catalog(Doc(Defs, $"'main':{{'type':'object',{body}}}"));

        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    [Trait("Spec", "LEX-9")]
    public void Create_ThreadStyleRecursion_IsAllowed()
    {
        var catalog = Catalog(Doc(Defs, """
            'threadView':{'type':'object','required':['post'],'properties':{
              'post':{'type':'ref','ref':'#postView'},
              'parent':{'type':'union','refs':['#threadView']},
              'replies':{'type':'array','items':{'type':'union','refs':['#threadView']}}}},
            'postView':{'type':'object','required':['uri'],'properties':{'uri':{'type':'string'},'reply':{'type':'ref','ref':'#postView'}}}
            """));

        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    [Trait("Spec", "LEX-9")]
    public void Create_DiamondOfRequiredReferences_IsNotACycle()
    {
        var catalog = Catalog(Doc(Defs, """
            'main':{'type':'object','required':['l','r'],'properties':{'l':{'type':'ref','ref':'#leaf'},'r':{'type':'ref','ref':'#leaf'}}},
            'leaf':{'type':'object','properties':{}}
            """));

        Assert.Empty(catalog.Diagnostics);
    }

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Create_TwoDocumentsWithTheSameNsid_Throws()
    {
        Assert.Throws<ArgumentException>(() => Catalog(Target(), Target()));
    }

    [Fact]
    [Trait("Spec", "LEX-6")]
    public void Documents_AreSortedByNsidAndFoundByTryGetDocument()
    {
        var catalog = Catalog(Doc("com.example.b", "'main':{'type':'token'}"), Doc("com.example.a", "'main':{'type':'token'}"));

        Assert.Equal(["com.example.a", "com.example.b"], catalog.Documents.Select(d => d.Id.Value));
        Assert.True(catalog.TryGetDocument(Nsid.Parse("com.example.b"), out var document));
        Assert.Equal("com.example.b", document.Id.Value);
        Assert.False(catalog.TryGetDocument(Nsid.Parse("com.example.c"), out _));
    }

    private sealed class FakeExternal(params ExternalDefinition[] definitions) : IExternalLexicons
    {
        public bool TryResolve(LexRefTarget target, [NotNullWhen(true)] out ExternalDefinition? definition) =>
            (definition = definitions.FirstOrDefault(d => d.Target == target)) is not null;
    }
}
