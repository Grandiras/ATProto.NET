using System.Text;
using ATProtoNet.Data;
using ATProtoNet.Lexicon;
using ATProtoNet.Lexicon.Validation;

namespace ATProtoNet.Tests.Lexicon.Validation;

public sealed class LexiconValidatorTests
{
    private static readonly Nsid Test = Nsid.Parse("com.example.test");
    private static readonly Nsid Other = Nsid.Parse("com.example.other");

    private static readonly LexiconValidator Validator = new(LexiconCatalog.Create(
    [
        Document("""
            {"lexicon": 1, "id": "com.example.test", "defs": {
              "main": {"type": "record", "key": "tid", "record": {"type": "object", "required": ["text"], "nullable": ["note"], "properties": {
                "text": {"type": "string"},
                "note": {"type": "string"},
                "embed": {"type": "union", "refs": ["#images", "com.example.other"]},
                "only": {"type": "union", "refs": ["#images"], "closed": true},
                "other": {"type": "ref", "ref": "com.example.other"}}}},
              "images": {"type": "object", "required": ["images"], "properties": {
                "images": {"type": "array", "items": {"type": "ref", "ref": "#image"}}}},
              "image": {"type": "object", "required": ["alt"], "properties": {"alt": {"type": "string"}}},
              "node": {"type": "object", "properties": {"child": {"type": "ref", "ref": "#node"}}},
              "flags": {"type": "array", "items": {"type": "boolean"}},
              "token": {"type": "token"}}}
            """),
        Document("""{"lexicon": 1, "id": "com.example.other", "defs": {"main": {"type": "object", "properties": {"n": {"type": "integer"}}}}}"""),
        Document("""{"lexicon": 1, "id": "com.example.self", "defs": {"main": {"type": "record", "key": "literal:self", "record": {"type": "object", "properties": {}}}}}"""),
        Document("""{"lexicon": 1, "id": "com.example.bynsid", "defs": {"main": {"type": "record", "key": "nsid", "record": {"type": "object", "properties": {}}}}}"""),
        Document("""{"lexicon": 1, "id": "com.example.linked", "defs": {"main": {"type": "object", "properties": {"self": {"type": "ref", "ref": "com.example.self"}}}}}"""),
        Document("""{"lexicon": 1, "id": "com.example.byany", "defs": {"main": {"type": "record", "key": "any", "record": {"type": "object", "properties": {}}}}}"""),
    ]));

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_NestedViolation_ReportsItsDataPath()
    {
        var result = Validator.Validate(new(Test, "main"), Value("""
            {"$type": "com.example.test", "text": "hi", "embed": {"$type": "com.example.test#images", "images": [{"alt": "a"}, {"alt": 1}]}}
            """));

        var violation = Assert.Single(result.Violations);
        Assert.Equal("$.embed.images[1].alt", violation.Path);
        Assert.Equal("Expected a string, got an integer.", violation.Message);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData("""{"self": {"$type": "com.example.self"}}""", true)]
    [InlineData("""{"self": {}}""", false)]
    [InlineData("""{"self": {"$type": "com.example.test"}}""", false)]
    public void Validate_RecordReachedThroughARef_ChecksItsType(string json, bool valid) =>
        Assert.Equal(valid, Validator.Validate(new(Nsid.Parse("com.example.linked"), "main"), Value(json)).IsValid);

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_MissingRequiredProperty_ReportsIt()
    {
        var result = Validator.Validate(new(Test, "main"), Value("""{"$type": "com.example.test"}"""));

        Assert.Equal([new LexiconViolation("$.text", "Required, but missing.")], result.Violations);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData("note", true)]
    [InlineData("text", false)]
    [InlineData("embed", false)]
    public void Validate_NullProperty_PassesOnlyWhenNullable(string property, bool valid)
    {
        var text = property == "text" ? "" : "\"text\": \"t\", ";
        var result = Validator.Validate(new(Test, "main"), Value($$"""{"$type": "com.example.test", {{text}}"{{property}}": null}"""));

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData("""{"type": "boolean"}""", "true", true)]
    [InlineData("""{"type": "boolean"}""", "1", false)]
    [InlineData("""{"type": "boolean", "const": true}""", "false", false)]
    [InlineData("""{"type": "integer"}""", "\"1\"", false)]
    [InlineData("""{"type": "integer", "minimum": 1, "maximum": 3}""", "0", false)]
    [InlineData("""{"type": "integer", "minimum": 1, "maximum": 3}""", "3", true)]
    [InlineData("""{"type": "integer", "minimum": 1, "maximum": 3}""", "4", false)]
    [InlineData("""{"type": "integer", "enum": [2, 4]}""", "4", true)]
    [InlineData("""{"type": "integer", "enum": [2, 4]}""", "3", false)]
    [InlineData("""{"type": "integer", "const": 7}""", "8", false)]
    [InlineData("""{"type": "integer", "default": 7}""", "8", true)]
    [InlineData("""{"type": "string", "enum": ["a", "b"]}""", "\"c\"", false)]
    [InlineData("""{"type": "string", "const": "a"}""", "\"a\"", true)]
    [InlineData("""{"type": "string", "knownValues": ["a"]}""", "\"z\"", true)]
    [InlineData("""{"type": "string", "maxLength": 2}""", "\"é\"", true)]
    [InlineData("""{"type": "string", "maxLength": 2}""", "\"éa\"", false)]
    [InlineData("""{"type": "string", "minLength": 3}""", "\"éa\"", true)]
    [InlineData("""{"type": "string", "maxGraphemes": 1}""", "\"👩‍👩‍👦‍👦\"", true)]
    [InlineData("""{"type": "string", "maxGraphemes": 1}""", "\"🇩🇪\"", true)]
    [InlineData("""{"type": "string", "maxGraphemes": 1}""", "\"é\"", true)]
    [InlineData("""{"type": "string", "maxGraphemes": 1}""", "\"ab\"", false)]
    [InlineData("""{"type": "string", "minGraphemes": 2}""", "\"🏳️‍🌈\"", false)]
    [InlineData("""{"type": "bytes", "minLength": 2, "maxLength": 3}""", """{"$bytes": "AAA"}""", true)]
    [InlineData("""{"type": "bytes", "minLength": 2, "maxLength": 3}""", """{"$bytes": "AA"}""", false)]
    [InlineData("""{"type": "bytes", "minLength": 2, "maxLength": 3}""", """{"$bytes": "AAAAAA"}""", false)]
    [InlineData("""{"type": "bytes"}""", "\"AA\"", false)]
    [InlineData("""{"type": "cid-link"}""", """{"$link": "bafyreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq"}""", true)]
    [InlineData("""{"type": "cid-link"}""", "\"bafyreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq\"", false)]
    [InlineData("""{"type": "array", "items": {"type": "integer"}, "minLength": 1, "maxLength": 2}""", "[1, 2]", true)]
    [InlineData("""{"type": "array", "items": {"type": "integer"}, "minLength": 1, "maxLength": 2}""", "[]", false)]
    [InlineData("""{"type": "array", "items": {"type": "integer"}, "minLength": 1, "maxLength": 2}""", "[1, 2, 3]", false)]
    [InlineData("""{"type": "array", "items": {"type": "integer"}}""", "[1, null]", false)]
    [InlineData("""{"type": "object", "properties": {"a": {"type": "integer"}}}""", """{"a": 1, "extra": "anything"}""", true)]
    [InlineData("""{"type": "unknown"}""", """{"anything": [1]}""", true)]
    [InlineData("""{"type": "unknown"}""", "true", false)]
    [InlineData("""{"type": "unknown"}""", """{"$bytes": "AA"}""", false)]
    [InlineData("""{"type": "unknown"}""", """{"$type": "blob", "ref": {"$link": "bafkreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq"}, "mimeType": "text/plain", "size": 1}""", false)]
    [InlineData("""{"type": "some-future-type"}""", "17", true)]
    public void Validate_FieldSchema_MatchesItsRules(string schema, string value, bool valid)
    {
        Assert.Equal(valid, ValidateField(schema, value).IsValid);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData("at-identifier", "alice.example.com", "123")]
    [InlineData("at-uri", "at://alice.example.com/com.example.test/3kznmn7xqxl22", "123")]
    [InlineData("cid", "bafyreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq", "123")]
    [InlineData("datetime", "2023-10-30T22:25:23Z", "2023-10-30")]
    [InlineData("did", "did:web:example.com", "123")]
    [InlineData("handle", "alice.example.com", "123")]
    [InlineData("language", "en-US", "123")]
    [InlineData("nsid", "com.example.test", "123")]
    [InlineData("record-key", "self", ".")]
    [InlineData("tid", "3kznmn7xqxl22", "000")]
    [InlineData("uri", "https://example.com/a", "123")]
    public void Validate_StringFormat_UsesTheIdentifierParser(string format, string valid, string invalid)
    {
        var schema = $$"""{"type": "string", "format": "{{format}}"}""";

        Assert.True(ValidateField(schema, $"\"{valid}\"").IsValid);
        Assert.Equal("Is not a valid " + format + ".", Assert.Single(ValidateField(schema, $"\"{invalid}\"").Violations).Message);
    }

    [Fact]
    [Trait("Spec", "LEX-19")]
    public void Validate_UnknownStringFormat_Passes()
    {
        Assert.True(ValidateField("""{"type": "string", "format": "future-format"}""", "\"anything\"").IsValid);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData("image/png", true)]
    [InlineData("IMAGE/PNG", true)]
    [InlineData("video/mp4", true)]
    [InlineData("text/plain", false)]
    public void Validate_BlobMimeType_MatchesAcceptPatterns(string mimeType, bool valid)
    {
        var blob = $$"""{"$type": "blob", "ref": {"$link": "bafkreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq"}, "mimeType": "{{mimeType}}", "size": 10}""";

        Assert.Equal(valid, ValidateField("""{"type": "blob", "accept": ["image/*", "video/mp4"]}""", blob).IsValid);
        Assert.True(ValidateField("""{"type": "blob", "accept": ["*/*"]}""", blob).IsValid);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData(100, true)]
    [InlineData(101, false)]
    public void Validate_BlobSize_IsCheckedAgainstMaxSize(int size, bool valid)
    {
        var blob = $$"""{"$type": "blob", "ref": {"$link": "bafkreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq"}, "mimeType": "image/png", "size": {{size}}}""";

        Assert.Equal(valid, ValidateField("""{"type": "blob", "maxSize": 100}""", blob).IsValid);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData("""{"cid": "bafkreiclp443lavogvhj3d2ob2cxbfuscni2k5jk7bebjzg7khl3esabwq", "mimeType": "image/png"}""", true)]
    [InlineData("""{"mimeType": "image/png", "size": 1}""", false)]
    [InlineData("\"image.png\"", false)]
    public void Validate_BlobShape_AcceptsTheDataModelAndLegacyForms(string value, bool valid)
    {
        Assert.Equal(valid, ValidateField("""{"type": "blob"}""", value).IsValid);
    }

    [Theory]
    [Trait("Spec", "LEX-19")]
    [InlineData("""{"$type": "com.example.unknown#thing", "x": 1}""", true)]
    [InlineData("""{"$type": "com.example.other", "n": 1}""", true)]
    [InlineData("""{"$type": "com.example.other#main", "n": 1}""", true)]
    [InlineData("""{"$type": "com.example.other", "n": "1"}""", false)]
    [InlineData("""{"n": 1}""", false)]
    [InlineData("1", false)]
    public void Validate_OpenUnion_ChecksKnownMembersOnly(string embed, bool valid)
    {
        var result = Validator.Validate(new(Test, "main"), Value($$"""{"$type": "com.example.test", "text": "t", "embed": {{embed}}}"""));

        Assert.Equal(valid, result.IsValid);
    }

    [Theory]
    [Trait("Spec", "LEX-19")]
    [InlineData("com.example.test#images", true)]
    [InlineData("com.example.unknown#thing", false)]
    public void Validate_ClosedUnion_RejectsUnlistedTypes(string type, bool valid)
    {
        var result = Validator.Validate(new(Test, "main"), Value($$$"""{"$type": "com.example.test", "text": "t", "only": {"$type": "{{{type}}}", "images": []}}"""));

        Assert.Equal(valid, result.IsValid);
        if (!valid) Assert.Equal("$.only.$type", Assert.Single(result.Violations).Path);
    }

    [Fact]
    [Trait("Spec", "LEX-19")]
    public void Validate_UnionMemberWithoutType_ReportsTheMissingType()
    {
        var result = Validator.Validate(new(Test, "main"), Value("""{"$type": "com.example.test", "text": "t", "embed": {"images": []}}"""));

        Assert.Equal("$.embed.$type", Assert.Single(result.Violations).Path);
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_CrossDocumentRef_UsesTheTargetDocument()
    {
        var result = Validator.Validate(new(Test, "main"), Value("""{"$type": "com.example.test", "text": "t", "other": {"n": true}}"""));

        Assert.Equal("$.other.n", Assert.Single(result.Violations).Path);
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_PropertyNameThatIsNoIdentifier_IsBracketedInThePath()
    {
        var result = ValidateField("""{"type": "object", "properties": {"cid-link": {"type": "integer"}}}""", """{"cid-link": "x"}""");

        Assert.Equal("$.v['cid-link']", Assert.Single(result.Violations).Path);
    }

    [Theory]
    [Trait("Spec", "LEX-18")]
    [InlineData("""{"$type": "com.example.other", "text": "t"}""", false)]
    [InlineData("""{"text": "t"}""", false)]
    [InlineData("""{"$type": "com.example.test#main", "text": "t"}""", true)]
    public void Validate_RecordType_MustNameTheCollection(string record, bool valid)
    {
        var result = Validator.Validate(new(Test, "main"), Value(record));

        Assert.Equal(valid, result.IsValid);
        if (!valid) Assert.Equal("$.$type", Assert.Single(result.Violations).Path);
    }

    [Theory]
    [Trait("Spec", "LEX-20")]
    [InlineData("com.example.test", "3kznmn7xqxl22", true)]
    [InlineData("com.example.test", "self", false)]
    [InlineData("com.example.self", "self", true)]
    [InlineData("com.example.self", "other", false)]
    [InlineData("com.example.bynsid", "com.example.x", true)]
    [InlineData("com.example.bynsid", "self", false)]
    [InlineData("com.example.byany", "any-key:1", true)]
    public void ValidateRecord_RecordKey_MatchesTheKeyType(string collection, string rkey, bool valid)
    {
        var record = Value(collection == "com.example.test" ? """{"$type": "com.example.test", "text": "t"}""" : $$"""{"$type": "{{collection}}"}""");

        var result = Validator.ValidateRecord(Nsid.Parse(collection), RecordKey.Parse(rkey), record);

        Assert.Equal(valid, result.IsValid);
        if (!valid) Assert.Equal("rkey", Assert.Single(result.Violations).Path);
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void ValidateRecord_CollectionNotARecord_Throws()
    {
        Assert.Throws<ArgumentException>(() => Validator.ValidateRecord(Other, RecordKey.Parse("self"), Value("{}")));
        Assert.Throws<ArgumentException>(() => Validator.ValidateRecord(Nsid.Parse("com.example.missing"), RecordKey.Parse("self"), Value("{}")));
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_UnknownDefinition_Throws()
    {
        Assert.Throws<ArgumentException>(() => Validator.Validate(new(Test, "missing"), Value("{}")));
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_Token_IsNotData()
    {
        Assert.False(Validator.Validate(new(Test, "token"), Value("\"com.example.test#token\"")).IsValid);
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_DataDeeperThanTheLimit_StopsWithOneViolation()
    {
        DataModelValue node = new DataModelMap([]);
        for (var i = 0; i < LexiconLimits.MaxValidationDepth + 10; i++) node = new DataModelMap([new("child", node)]);

        var result = Validator.Validate(new(Test, "node"), node);

        var violation = Assert.Single(result.Violations);
        Assert.Equal(LexiconLimits.MaxValidationDepth, violation.Path.Split('.').Length - 1);
        Assert.StartsWith("Nested deeper than", violation.Message, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void Validate_ManyViolations_StopsAtTheLimit()
    {
        var flags = new DataModelArray(Enumerable.Range(0, 250).Select(i => (DataModelValue)new DataModelInteger(i)));

        var result = Validator.Validate(new(Test, "flags"), flags);

        Assert.Equal(LexiconLimits.MaxValidationErrors, result.Violations.Count);
        Assert.Equal("$[99]", result.Violations[^1].Path);
    }

    [Fact]
    [Trait("Spec", "LEX-18")]
    public void ThrowIfInvalid_Violations_ThrowsWithThem()
    {
        var result = Validator.Validate(new(Test, "main"), Value("""{"$type": "com.example.test"}"""));

        var exception = Assert.Throws<LexiconValidationException>(result.ThrowIfInvalid);
        Assert.Equal(result.Violations, exception.Violations);
        Assert.IsAssignableFrom<AtProtoException>(exception);
    }

    private static LexiconValidationResult ValidateField(string schema, string value)
    {
        var validator = new LexiconValidator(LexiconCatalog.Create(
            [Document("""{"lexicon": 1, "id": "com.example.field", "defs": {"main": {"type": "object", "properties": {"v": """ + schema + "}}}}")]));
        return validator.Validate(new(Nsid.Parse("com.example.field"), "main"), Value($$"""{"v": {{value}}}"""));
    }

    private static LexiconDocument Document(string json) => LexiconDocument.Parse(Encoding.UTF8.GetBytes(json));

    private static DataModelValue Value(string json) => DataModelValue.FromJson(Encoding.UTF8.GetBytes(json));
}
