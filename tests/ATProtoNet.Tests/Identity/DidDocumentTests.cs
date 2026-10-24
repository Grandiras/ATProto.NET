using ATProtoNet.Identity;
using ATProtoNet.TestKit;
using static ATProtoNet.Tests.Identity.IdentityFixtures;

namespace ATProtoNet.Tests.Identity;

/// <summary>Reading a DID document: the shapes real documents use, and what AT Protocol takes from it.</summary>
public class DidDocumentTests
{
    [Fact]
    [Trait("Spec", "IDN-12")]
    public void Parse_PlcDirectoryDocument_ReadsEveryAtprotoField()
    {
        var document = Parse(AtprotoDotCom);

        Assert.Equal(Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"), document.Id);
        Assert.Equal(Handle.Parse("atproto.com"), document.GetHandle());
        Assert.Equal("did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", document.GetSigningKey());
        Assert.Equal(new Uri("https://enoki.us-east.host.bsky.network"), document.GetPdsEndpoint());
        Assert.Equal(System.Text.Encoding.UTF8.GetBytes(AtprotoDotCom), document.Json.ToArray());
    }

    [Fact]
    public void Lookups_RepeatedOnASharedDocument_AnswerWithTheSameValues()
    {
        var document = Parse(AtprotoDotCom);

        Assert.Same(document.GetPdsEndpoint(), document.GetPdsEndpoint());
        var first = document.TryGetVerificationKey("#atproto");
        Assert.Equal(DidDocumentEntryStatus.Found, first.Status);
        Assert.Equal(first, document.TryGetVerificationKey("#atproto"));
        Assert.Same(first.Value, document.GetSigningKey());
    }

    [Theory]
    [Trait("Spec", "IDN-12")]
    [InlineData("""{"@context":"https://www.w3.org/ns/did/v1","id":"did:web:example.com"}""")]
    [InlineData("""{"@context":["https://www.w3.org/ns/did/v1",{"@vocab":"x"}],"id":"did:web:example.com"}""")]
    [InlineData("""{"id":"did:web:example.com","@context":{"nested":[1,{"a":null}]},"unknown":{"b":[true]}}""")]
    public void Parse_AnyContextAndUnknownProperties_AreTolerated(string json) =>
        Assert.Equal("did:web:example.com", Parse(json).Id.Value);

    [Fact]
    [Trait("Spec", "IDN-12")]
    public void Parse_StructuredServiceEndpoint_IsReadAsAbsent()
    {
        var document = Parse("""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":{"origins":["https://a.example"]}}
            ]}
            """);

        Assert.Null(document.Services[0].Endpoint);
        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetServiceEndpoint("atproto_pds").Status);
    }

    [Theory]
    [Trait("Spec", "IDN-12")]
    [InlineData("""{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz"}""")]
    [InlineData("""{"id":"did:web:x.com","alsoKnownAs":null,"verificationMethod":null,"service":null}""")]
    public void Parse_MissingOrNullLists_AreEmptyAndLookupsWork(string json)
    {
        var document = Parse(json);

        Assert.Empty(document.AlsoKnownAs);
        Assert.Empty(document.VerificationMethods);
        Assert.Empty(document.Services);
        Assert.Null(document.GetHandle());
        Assert.Null(document.GetSigningKey());
        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetServiceEndpoint("#atproto_pds").Status);
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetVerificationKey("#atproto").Status);
    }

    [Theory]
    [Trait("Spec", "IDN-12")]
    [InlineData("""{"id":"not a did"}""")]
    [InlineData("""{"id":"did:web:x.com","id":42}""")]
    [InlineData("""{"alsoKnownAs":[]}""")]
    [InlineData("""{"id":"did:web:x.com","alsoKnownAs":[null]}""")]
    [InlineData("""{"id":"did:web:x.com","alsoKnownAs":["at://x.com",7]}""")]
    [InlineData("""{"id":"did:web:x.com","verificationMethod":[null]}""")]
    [InlineData("""{"id":"did:web:x.com","verificationMethod":[{"type":"Multikey"}]}""")]
    [InlineData("""{"id":"did:web:x.com","verificationMethod":[{"id":"#atproto","type":null}]}""")]
    [InlineData("""{"id":"did:web:x.com","service":[null]}""")]
    [InlineData("""{"id":"did:web:x.com","service":[{"id":"#atproto_pds"}]}""")]
    [InlineData("""{"id":"did:web:x.com","service":"https://x.com"}""")]
    [InlineData("""{"id":"did:web:x.com"} trailing""")]
    [InlineData("""[{"id":"did:web:x.com"}]""")]
    [InlineData("null")]
    [InlineData("not json {{{")]
    [InlineData("")]
    public void Parse_UntrustworthyValue_ThrowsFormatException(string json) =>
        Assert.Throws<FormatException>(() => Parse(json));

    // ── GetHandle ────────────────────────────────────────────

    [Theory]
    [Trait("Spec", "IDN-17")]
    [InlineData("""["at://alice.example.com"]""", "alice.example.com")]
    [InlineData("""["at://Alice.Example.com"]""", "alice.example.com")]
    [InlineData("""["https://alice.example.com","at://alice.example.com"]""", "alice.example.com")]
    [InlineData("""["AT://alice.example.com"]""", null)]
    [InlineData("""["at://not a handle","at://second.example.com"]""", null)]
    [InlineData("""["at://@alice.example.com"]""", null)]
    [InlineData("""["at://did:plc:ewvi7nxzyoun6zhxrhs64oiz"]""", null)]
    [InlineData("""["at://single"]""", null)]
    [InlineData("""[]""", null)]
    public void GetHandle_ReadsOnlyTheFirstAtUriEntry(string alsoKnownAs, string? expected)
    {
        var document = Parse($$"""{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","alsoKnownAs":{{alsoKnownAs}}}""");

        Assert.Equal(expected, document.GetHandle()?.Value);
    }

    // ── Service endpoints ────────────────────────────────────

    [Theory]
    [Trait("Spec", "IDN-13")]
    [InlineData("#atproto_pds")]
    [InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto_pds")]
    public void TryGetServiceEndpoint_MatchesBareAndDidQualifiedIds(string id)
    {
        var document = Parse($$"""
            {"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","service":[
              {"id":"{{id}}","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        Assert.Equal(new Uri("https://pds.example.com"), document.TryGetServiceEndpoint("atproto_pds").Value);
        Assert.Equal(new Uri("https://pds.example.com"), document.TryGetServiceEndpoint("#atproto_pds", "AtprotoPersonalDataServer").Value);
        Assert.Equal(new Uri("https://pds.example.com"), document.GetPdsEndpoint());
    }

    [Theory]
    [Trait("Spec", "IDN-13")]
    [InlineData("did:plc:zzzzzzzzzzzzzzzzzzzzzzzz#atproto_pds")]
    [InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oizz#atproto_pds")]
    [InlineData("atproto_pds")]
    [InlineData("#atproto_pds_other")]
    public void TryGetServiceEndpoint_IdOfAnotherDidOrFragment_DoesNotMatch(string id)
    {
        var document = Parse($$"""
            {"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","service":[
              {"id":"{{id}}","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetServiceEndpoint("#atproto_pds").Status);
    }

    [Fact]
    [Trait("Spec", "IDN-13")]
    [Trait("Spec", "IDN-14")]
    public void GetPdsEndpoint_WrongType_IsAbsentButTheStrictLookupSaysMalformed()
    {
        var document = Parse("""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_pds","type":"SomethingElse","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetServiceEndpoint("#atproto_pds", DidDocument.PdsServiceType).Status);
        Assert.Equal(new Uri("https://pds.example.com"), document.TryGetServiceEndpoint("#atproto_pds").Value);
    }

    [Theory]
    [Trait("Spec", "IDN-14")]
    [Trait("Spec", "IDN-15")]
    [InlineData("https://host.example.com", DidDocumentEntryStatus.Found)]
    [InlineData("http://host.example.com:2583", DidDocumentEntryStatus.Found)]
    [InlineData("not a url", DidDocumentEntryStatus.Malformed)]
    [InlineData("ftp://host.example.com", DidDocumentEntryStatus.Malformed)]
    public void TryGetServiceEndpoint_TellsAMalformedEntryFromAnAbsentOne(string endpoint, DidDocumentEntryStatus expected)
    {
        var document = Parse($$"""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_labeler","type":"AtprotoLabeler","serviceEndpoint":"{{endpoint}}"},
              {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        var found = document.TryGetServiceEndpoint(DidDocument.LabelerServiceId, DidDocument.LabelerServiceType);
        Assert.Equal(expected, found.Status);
        Assert.Equal(expected == DidDocumentEntryStatus.Found ? new Uri(endpoint) : null, found.Value);
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetServiceEndpoint("#atproto_space_syncer").Status);
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetServiceEndpoint("#atproto_pds", "OtherType").Status);
    }

    [Theory]
    [Trait("Spec", "IDN-15")]
    [InlineData("pds.example.com")]
    [InlineData("ftp://pds.example.com")]
    [InlineData("wss://pds.example.com")]
    [InlineData("/relative")]
    [InlineData("")]
    public void GetPdsEndpoint_NotAnAbsoluteHttpUrl_IsAbsent(string endpoint)
    {
        var document = Parse($$"""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"{{endpoint}}"}
            ]}
            """);

        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(endpoint, document.Services[0].Endpoint);
    }

    [Fact]
    [Trait("Spec", "IDN-13")]
    public void Lookups_WithDuplicateIds_TakeTheFirstEntry()
    {
        var document = Parse($$"""
            {"id":"did:web:example.com",
             "verificationMethod":[
               {"id":"#atproto","type":"Multikey","publicKeyMultibase":"{{TestKeys.K256Multibase}}"},
               {"id":"did:web:example.com#atproto","type":"Multikey","publicKeyMultibase":"{{TestKeys.P256Multibase}}"},
               {"id":"#atproto_label","type":"JsonWebKey2020","publicKeyMultibase":"{{TestKeys.K256Multibase}}"},
               {"id":"#atproto_label","type":"Multikey","publicKeyMultibase":"{{TestKeys.P256Multibase}}"}],
             "service":[
               {"id":"#atproto_pds","type":"Other","serviceEndpoint":"https://first.example"},
               {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://second.example"}]}
            """);

        Assert.Equal(TestKeys.K256DidKey, document.GetSigningKey());
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetVerificationKey(DidDocument.LabelKeyId).Status);
        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetServiceEndpoint("#atproto_pds", "AtprotoPersonalDataServer").Status);
    }

    [Fact]
    public void Lookups_EmptyFragment_Throw()
    {
        var document = Parse(AtprotoDotCom);

        Assert.Throws<ArgumentException>(() => document.TryGetVerificationKey(""));
        Assert.Throws<ArgumentException>(() => document.TryGetServiceEndpoint(""));
    }

    [Fact]
    [Trait("Spec", "IDN-18")]
    [Trait("Spec", "IDN-55")]
    public void Constants_AreTheWellKnownIdsAndTypes()
    {
        Assert.Equal("#atproto", DidDocument.SigningKeyId);
        Assert.Equal("#atproto_label", DidDocument.LabelKeyId);
        Assert.Equal("#atproto_kem", DidDocument.KemKeyId);
        Assert.Equal("#atproto_pds", DidDocument.PdsServiceId);
        Assert.Equal("AtprotoPersonalDataServer", DidDocument.PdsServiceType);
        Assert.Equal("#atproto_labeler", DidDocument.LabelerServiceId);
        Assert.Equal("AtprotoLabeler", DidDocument.LabelerServiceType);
    }
}
