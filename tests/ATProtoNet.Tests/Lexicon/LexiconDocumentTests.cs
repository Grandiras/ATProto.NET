using System.Text;
using System.Text.Json;
using ATProtoNet.Lexicon;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>Parsing a Lexicon document into the schema model (LEX-2–LEX-5, LEX-23).</summary>
public sealed class LexiconDocumentTests
{
    private const string Id = "com.example.thing";

    private static (bool Ok, IReadOnlyList<LexiconDiagnostic> Diagnostics) Try(string json) =>
        (LexiconDocument.TryParse(Lex.Bytes(json), out _, out var diagnostics), diagnostics);

    private static string MainDef(string def, string extra = "") => Lex.Defs(Id, $"'main':{def}{extra}");

    private static string Fields(string properties) => Lex.Record(Id, properties);

    private static LexSchema Field(string schema) => ((LexRecord)Lex.Doc(Fields($"'f':{schema}")).Main!).Record.Properties["f"];

    [Fact]
    [Trait("Spec", "LEX-2")]
    public void Parse_Document_ExposesIdRevisionDescriptionAndDefsInOrder()
    {
        var document = Lex.Doc(Lex.Defs(Id, "'zeta':{'type':'token'},'main':{'type':'token'},'alpha':{'type':'string'}", ",'revision':3,'description':'A thing'"));

        Assert.Equal(1, document.LexiconVersion);
        Assert.Equal(Id, document.Id.Value);
        Assert.Equal(3, document.Revision);
        Assert.Equal("A thing", document.Description);
        Assert.Equal(["zeta", "main", "alpha"], document.Defs.Keys);
        Assert.IsType<LexToken>(document.Main);
    }

    [Fact]
    [Trait("Spec", "LEX-2")]
    public void Parse_Document_KeepsTheExactSourceBytes()
    {
        var source = Encoding.UTF8.GetBytes("{ \"id\" :\"com.example.thing\",\n\t\"defs\":{\"main\":{ \"type\":\"token\",\"description\":\"\\u00e9\" }},   \"lexicon\":1 }\n");

        var document = LexiconDocument.Parse(source);

        Assert.True(document.SourceUtf8.Span.SequenceEqual(source));
    }

    public static TheoryData<string, Type> SchemaTypes() => new()
    {
        { "{'type':'string'}", typeof(LexString) },
        { "{'type':'integer'}", typeof(LexInteger) },
        { "{'type':'boolean'}", typeof(LexBoolean) },
        { "{'type':'bytes'}", typeof(LexBytes) },
        { "{'type':'cid-link'}", typeof(LexCidLink) },
        { "{'type':'blob'}", typeof(LexBlob) },
        { "{'type':'array','items':{'type':'string'}}", typeof(LexArray) },
        { "{'type':'unknown'}", typeof(LexUnknown) },
        { "{'type':'ref','ref':'com.example.other'}", typeof(LexRef) },
        { "{'type':'union','refs':['com.example.other']}", typeof(LexUnion) },
        { "{'type':'object','properties':{}}", typeof(LexObject) },
    };

    [Theory]
    [MemberData(nameof(SchemaTypes))]
    [Trait("Spec", "LEX-2")]
    public void Parse_FieldType_BecomesItsRecordTypeAndKeepsRaw(string schema, Type expected)
    {
        var field = Field(schema);

        Assert.IsType(expected, field);
        Assert.Equal(JsonValueKind.Object, field.Raw.ValueKind);
        Assert.Equal(JsonDocument.Parse(schema.Replace('\'', '"')).RootElement.GetProperty("type").GetString(), field.Raw.GetProperty("type").GetString());
    }

    public static TheoryData<string, Type> DefinitionTypes() => new()
    {
        { "{'type':'record','key':'tid','record':{'type':'object','properties':{}}}", typeof(LexRecord) },
        { "{'type':'query'}", typeof(LexQuery) },
        { "{'type':'procedure'}", typeof(LexProcedure) },
        { "{'type':'subscription'}", typeof(LexSubscription) },
        { "{'type':'permission-set','permissions':[]}", typeof(LexPermissionSet) },
        { "{'type':'space','collections':[]}", typeof(LexSpace) },
        { "{'type':'token'}", typeof(LexToken) },
    };

    [Theory]
    [MemberData(nameof(DefinitionTypes))]
    [Trait("Spec", "LEX-2")]
    public void Parse_MainDefinition_BecomesItsRecordType(string def, Type expected)
    {
        var main = Lex.Doc(MainDef(def)).Main;

        Assert.IsType(expected, main);
        Assert.Equal(JsonValueKind.Object, main!.Raw.ValueKind);
    }

    [Fact]
    [Trait("Spec", "LEX-2")]
    public void Parse_Strings_KeepEveryConstraint()
    {
        var field = Assert.IsType<LexString>(Field("{'type':'string','format':'uri','minLength':1,'maxLength':9,'minGraphemes':2,'maxGraphemes':8,'enum':['a'],'knownValues':['b'],'const':'a','default':'a','description':'d'}"));

        Assert.Equal(("uri", 1, 9, 2, 8), (field.Format, field.MinLength, field.MaxLength, field.MinGraphemes, field.MaxGraphemes));
        Assert.Equal(["a"], field.Enum);
        Assert.Equal(["b"], field.KnownValues);
        Assert.Equal(("a", "a", "d"), (field.Const, field.Default, field.Description));
    }

    [Fact]
    [Trait("Spec", "LEX-2")]
    public void Parse_Query_KeepsParametersBodiesAndErrors()
    {
        var query = Assert.IsType<LexQuery>(Lex.Doc(MainDef(
            "{'type':'query','parameters':{'type':'params','required':['a'],'properties':{'a':{'type':'string'}}},'output':{'encoding':'application/json','schema':{'type':'ref','ref':'#o'}},'errors':[{'name':'Gone','description':'x'}]}",
            ",'o':{'type':'object','properties':{}}")).Main);

        Assert.Equal(["a"], query.Parameters!.Required);
        Assert.Equal("application/json", query.Output!.Encoding);
        Assert.IsType<LexRef>(query.Output.Schema);
        Assert.Equal(new LexError("Gone", "x"), Assert.Single(query.Errors));
    }

    [Fact]
    [Trait("Spec", "LEX-2")]
    public void Parse_Record_KeepsKeyAndObjectDescription()
    {
        var record = Assert.IsType<LexRecord>(Lex.Doc(MainDef("{'type':'record','key':'literal:self','description':'r','record':{'type':'object','description':'o','nullable':['a'],'properties':{'a':{'type':'string'}}}}")).Main);

        Assert.Equal(("literal:self", "self", "r", "o"), (record.Key.Text, record.Key.Literal, record.Description, record.Record.Description));
        Assert.Equal(["a"], record.Record.Nullable);
    }

    public static TheoryData<string, string, string> Violations() => new()
    {
        { "lexicon is not 1", """{'lexicon':2,'id':'com.example.thing','defs':{'main':{'type':'token'}}}""", "LXL001 /lexicon" },
        { "lexicon missing", """{'id':'com.example.thing','defs':{'main':{'type':'token'}}}""", "LXL001 /lexicon" },
        { "id is not an NSID", """{'lexicon':1,'id':'nope','defs':{'main':{'type':'token'}}}""", "LXL001 /id" },
        { "defs missing", """{'lexicon':1,'id':'com.example.thing'}""", "LXL001 /defs" },
        { "defs empty", """{'lexicon':1,'id':'com.example.thing','defs':{}}""", "LXL001 /defs" },
        { "primary type not main", """{'lexicon':1,'id':'com.example.thing','defs':{'other':{'type':'query'}}}""", "LXL002 /defs/other" },
        { "named ref", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token'},'r':{'type':'ref','ref':'com.example.other'}}}""", "LXL004 /defs/r" },
        { "named unknown", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token'},'u':{'type':'unknown'}}}""", "LXL004 /defs/u" },
        { "named params", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token'},'p':{'type':'params','properties':{}}}}""", "LXL004 /defs/p" },
        { "primary type as a property", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'object','properties':{'q':{'type':'query'}}}}}""", "LXL003 /defs/main/properties/q" },
        { "token as a property", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'object','properties':{'t':{'type':'token'}}}}}""", "LXL003 /defs/main/properties/t" },
        { "params property of the wrong type", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'query','parameters':{'type':'params','properties':{'o':{'type':'object','properties':{}}}}}}}""", "LXL007 /defs/main/parameters/properties/o" },
        { "params array of objects", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'query','parameters':{'type':'params','properties':{'o':{'type':'array','items':{'type':'object','properties':{}}}}}}}}""", "LXL007 /defs/main/parameters/properties/o" },
        { "record key invalid", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'record','key':'nope','record':{'type':'object','properties':{}}}}}""", "LXL003 /defs/main/key" },
        { "record without record", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'record','key':'tid'}}}""", "LXL003 /defs/main/record" },
        { "string maxLength not an integer", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'string','maxLength':'x'}}}""", "LXL003 /defs/main/maxLength" },
        { "body schema not an object", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'query','output':{'encoding':'a/b','schema':{'type':'string'}}}}}""", "LXL003 /defs/main/output/schema" },
        { "body encoding missing", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'query','output':{}}}}""", "LXL003 /defs/main/output/encoding" },
        { "union without refs", """{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'object','properties':{'u':{'type':'union'}}}}}""", "LXL003 /defs/main/properties/u/refs" },
        { "not JSON", "{'lexicon':", "LXL001 " },
        { "not an object", "[]", "LXL001 " },
    };

    [Theory]
    [MemberData(nameof(Violations))]
    [Trait("Spec", "LEX-3")]
    public void Parse_Violation_IsAnErrorWithItsRuleAndPointer(string name, string json, string ruleAndPointer)
    {
        _ = name;

        var (ok, diagnostics) = Try(json);

        Assert.False(ok);
        Assert.Contains(diagnostics, d => d.Severity == LexiconSeverity.Error && $"{d.RuleId} {d.Pointer}" == ruleAndPointer);
        Assert.Throws<FormatException>(() => LexiconDocument.Parse(Lex.Bytes(json)));
    }

    [Fact]
    [Trait("Spec", "LEX-3")]
    public void Parse_Violation_MessageNamesTheFirstError()
    {
        var e = Assert.Throws<FormatException>(() => LexiconDocument.Parse(Lex.Bytes("""{'lexicon':1,'id':'nope','defs':{'main':{'type':'token'}}}""")));

        Assert.Contains("LXL001", e.Message, StringComparison.Ordinal);
        Assert.Contains("/id", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "LEX-3")]
    public void Parse_DocumentOverTheSizeLimit_IsAnError()
    {
        var (ok, diagnostics) = (LexiconDocument.TryParse(new byte[LexiconLimits.MaxLexiconDocumentBytes + 1], out _, out var found), found);

        Assert.False(ok);
        Assert.Equal(LexiconRules.DocumentShape, Assert.Single(diagnostics).RuleId);
    }

    [Fact]
    [Trait("Spec", "LEX-3")]
    public void Parse_ParamsOfBooleansIntegersStringsAndTheirArrays_IsValid()
    {
        var (ok, diagnostics) = Try(MainDef("{'type':'query','parameters':{'type':'params','properties':{'b':{'type':'boolean'},'i':{'type':'integer'},'s':{'type':'string'},'l':{'type':'array','items':{'type':'string'}}}}}"));

        Assert.True(ok);
        Assert.Empty(diagnostics);
    }

    [Fact]
    [Trait("Spec", "LEX-3")]
    public void Parse_NamedUnion_IsAcceptedWithAWarning()
    {
        var (ok, diagnostics) = Try(MainDef("{'type':'token'}", ",'u':{'type':'union','refs':['com.example.other']}"));

        Assert.True(ok);
        var warning = Assert.Single(diagnostics);
        Assert.Equal((LexiconRules.NamedUnion, LexiconSeverity.Warning, "/defs/u"), (warning.RuleId, warning.Severity, warning.Pointer));
    }

    [Fact]
    [Trait("Spec", "LEX-4")]
    public void Parse_UnknownType_IsKeptWithAWarning()
    {
        var (ok, diagnostics) = Try(Lex.Defs(Id, "'main':{'type':'token'},'x':{'type':'hologram','size':3}"));
        var document = Lex.Doc(Lex.Defs(Id, "'main':{'type':'token'},'x':{'type':'hologram','size':3}"));

        Assert.True(ok);
        var warning = Assert.Single(diagnostics);
        Assert.Equal((LexiconRules.UnknownType, LexiconSeverity.Warning, "/defs/x/type"), (warning.RuleId, warning.Severity, warning.Pointer));
        Assert.Equal("hologram", Assert.IsType<LexUnknownType>(document.Defs["x"]).TypeName);
    }

    [Fact]
    [Trait("Spec", "LEX-4")]
    public void Parse_UnknownFieldType_IsKeptAsALexUnknownType()
    {
        Assert.Equal("hologram", Assert.IsType<LexUnknownType>(Field("{'type':'hologram'}")).TypeName);
    }

    [Theory]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':null}}""", "/defs/main")]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token'},'x':null}}""", "/defs/x")]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'object','properties':{'p':null}}}}""", "/defs/main/properties/p")]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'object','properties':{'p':{'type':'array','items':null}}}}}""", "/defs/main/properties/p/items")]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'query','output':{'encoding':'a/b','schema':null}}}}""", "/defs/main/output/schema")]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'object','properties':{'p':{'type':'null'}}}}}""", "/defs/main/properties/p/type")]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'record','key':'tid','record':null}}}""", "/defs/main/record")]
    [InlineData("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'record','key':'tid','record':[]}}}""", "/defs/main/record")]
    [Trait("Spec", "LEX-4")]
    public void Parse_NullSchema_IsAnErrorWhereverItIs(string json, string pointer)
    {
        var (ok, diagnostics) = Try(json);

        Assert.False(ok);
        Assert.Contains(diagnostics, d => d.Severity == LexiconSeverity.Error && d.RuleId == LexiconRules.InvalidSchema && d.Pointer == pointer);
    }

    [Fact]
    [Trait("Spec", "LEX-5")]
    public void Parse_LocalizedFields_AreKeptAsWritten()
    {
        var set = Assert.IsType<LexPermissionSet>(Lex.Doc(Lex.Defs("com.example.feed.auth",
            "'main':{'type':'permission-set','title':'T','title:lang':{'ja':'基本','pt-BR':'Básico'},'detail':'D','detail:lang':{'de':'Beschreibung'},'permissions':[]}")).Main);
        var space = Assert.IsType<LexSpace>(Lex.Doc(Lex.Defs("com.example.forum", "'main':{'type':'space','name':'Forum','name:lang':{'es':'Foro'},'collections':[]}")).Main);

        Assert.Equal(new Dictionary<string, string> { ["ja"] = "基本", ["pt-BR"] = "Básico" }, set.TitleLang);
        Assert.Equal(new Dictionary<string, string> { ["de"] = "Beschreibung" }, set.DetailLang);
        Assert.Equal(new Dictionary<string, string> { ["es"] = "Foro" }, space.NameLang);
        Assert.Equal(JsonValueKind.Object, set.Raw.GetProperty("title:lang").ValueKind);
    }

    [Fact]
    [Trait("Spec", "LEX-5")]
    public void Parse_LocalizedFieldThatIsNotAMapOfStrings_IsAnError()
    {
        var (ok, _) = Try(Lex.Defs("com.example.forum", "'main':{'type':'space','name:lang':{'es':1},'collections':[]}"));

        Assert.False(ok);
    }

    [Fact]
    [Trait("Spec", "LEX-23")]
    public void ComputeRecordCid_IgnoresKeyOrderWhitespaceAndAnExistingType()
    {
        var a = Lex.Doc("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token'}}}""");
        var b = Lex.Doc("""{ 'defs' : { 'main' : { 'type' : 'token' } }, '$type':'com.atproto.lexicon.schema', 'id':'com.example.thing', 'lexicon':1 }""");

        Assert.Equal(a.ComputeRecordCid(), b.ComputeRecordCid());
    }

    [Fact]
    [Trait("Spec", "LEX-23")]
    public void ComputeRecordCid_ChangedContent_ChangesTheCid()
    {
        var a = Lex.Doc("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token'}}}""");
        var b = Lex.Doc("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token','description':'x'}}}""");

        Assert.NotEqual(a.ComputeRecordCid(), b.ComputeRecordCid());
    }

    [Fact]
    [Trait("Spec", "LEX-23")]
    public void ComputeRecordCid_DocumentWithAFloat_ThrowsFormatException()
    {
        var document = Lex.Doc("""{'lexicon':1,'id':'com.example.thing','defs':{'main':{'type':'token'}},'ratio':1.5}""");

        Assert.Throws<FormatException>(() => document.ComputeRecordCid());
    }
}
