using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Reading a DID document: the wire shapes DID Core allows, and the three things AT Protocol
/// takes from it — the claimed handle, the signing key and service endpoints.
/// </summary>
public class DidDocumentTests
{
    private static DidDocument Parse(string json) =>
        JsonSerializer.Deserialize<DidDocument>(json, AtProtoJsonDefaults.Options)!;

    [Fact]
    public void Deserialize_PlcDirectoryDocument_ReadsEveryAtprotoField()
    {
        var document = Parse(DidDocs.AtprotoDotCom);

        Assert.Equal(Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz"), document.Id);
        Assert.Equal(3, document.Context!.Count);
        Assert.Equal(Handle.Parse("atproto.com"), document.GetHandle());
        Assert.Equal("did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", document.GetSigningKey());
        Assert.Equal(new Uri("https://enoki.us-east.host.bsky.network"), document.GetPdsEndpoint());
    }

    [Fact]
    public void Deserialize_SingleStringContextAndInlineContextObjects_AreTolerated()
    {
        var single = Parse("""{"@context":"https://www.w3.org/ns/did/v1","id":"did:web:example.com"}""");
        var mixed = Parse("""{"@context":["https://www.w3.org/ns/did/v1",{"@vocab":"x"}],"id":"did:web:example.com"}""");

        Assert.Equal(["https://www.w3.org/ns/did/v1"], single.Context);
        Assert.Equal(["https://www.w3.org/ns/did/v1"], mixed.Context);
    }

    [Fact]
    public void Deserialize_StructuredServiceEndpoint_IsReadAsAbsent()
    {
        var document = Parse("""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":{"origins":["https://a.example"]}}
            ]}
            """);

        Assert.Null(document.Service[0].Endpoint);
        Assert.Null(document.GetPdsEndpoint());
    }

    [Fact]
    public void Deserialize_MissingOptionalLists_AreEmpty()
    {
        var document = Parse("""{"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz"}""");

        Assert.Empty(document.AlsoKnownAs);
        Assert.Empty(document.VerificationMethod);
        Assert.Empty(document.Service);
        Assert.Null(document.GetHandle());
        Assert.Null(document.GetSigningKey());
        Assert.Null(document.GetPdsEndpoint());
    }

    [Fact]
    public void Deserialize_InvalidId_Throws() =>
        Assert.ThrowsAny<Exception>(() => Parse("""{"id":"not a did"}"""));

    [Fact]
    public void Serialize_RoundTripsTheWireShape()
    {
        var document = Parse(DidDocs.AtprotoDotCom);

        var again = Parse(JsonSerializer.Serialize(document, AtProtoJsonDefaults.Options));

        Assert.Equal(document.Id, again.Id);
        Assert.Equal(document.Context, again.Context);
        Assert.Equal(document.GetSigningKey(), again.GetSigningKey());
        Assert.Equal(document.GetPdsEndpoint(), again.GetPdsEndpoint());
    }

    // ── GetHandle ────────────────────────────────────────────

    [Theory]
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

    // ── GetServiceEndpoint ───────────────────────────────────

    [Theory]
    [InlineData("#atproto_pds")]
    [InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto_pds")]
    public void GetServiceEndpoint_MatchesBareAndDidQualifiedIds(string id)
    {
        var document = Parse($$"""
            {"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","service":[
              {"id":"{{id}}","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        Assert.Equal(new Uri("https://pds.example.com"), document.GetServiceEndpoint("atproto_pds"));
        Assert.Equal(new Uri("https://pds.example.com"), document.GetServiceEndpoint("#atproto_pds", "AtprotoPersonalDataServer"));
    }

    [Fact]
    public void GetServiceEndpoint_AnotherDidsQualifiedId_DoesNotMatch()
    {
        var document = Parse("""
            {"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","service":[
              {"id":"did:plc:zzzzzzzzzzzzzzzzzzzzzzzz#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        Assert.Null(document.GetPdsEndpoint());
    }

    [Fact]
    public void GetServiceEndpoint_WrongType_DoesNotMatch()
    {
        var document = Parse("""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_pds","type":"SomethingElse","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(new Uri("https://pds.example.com"), document.GetServiceEndpoint("#atproto_pds"));
    }

    [Theory]
    [InlineData("https://host.example.com", DidDocumentEntryStatus.Found)]
    [InlineData("not a url", DidDocumentEntryStatus.Malformed)]
    [InlineData("ftp://host.example.com", DidDocumentEntryStatus.Malformed)]
    public void TryGetServiceEndpoint_TellsAMalformedEntryFromAnAbsentOne(string endpoint, DidDocumentEntryStatus expected)
    {
        var document = Parse($$"""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_space_host","type":"AtprotoSpaceHost","serviceEndpoint":"{{endpoint}}"},
              {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://pds.example.com"}
            ]}
            """);

        Assert.Equal(expected, document.TryGetServiceEndpoint("#atproto_space_host", null, out var found));
        Assert.Equal(expected == DidDocumentEntryStatus.Found ? new Uri(endpoint) : null, found);
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetServiceEndpoint("#atproto_space_syncer", null, out _));
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetServiceEndpoint("#atproto_pds", "OtherType", out _));
    }

    [Fact]
    public void TryGetServiceEndpoint_StructuredEndpoint_IsMalformed()
    {
        var document = Parse("""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_space_host","type":"AtprotoSpaceHost","serviceEndpoint":{"origins":["https://a.example"]}}
            ]}
            """);

        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetServiceEndpoint("atproto_space_host", null, out _));
    }

    [Theory]
    [InlineData("Multikey", "zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", DidDocumentEntryStatus.Found)]
    [InlineData("Multikey", "zNotAKey", DidDocumentEntryStatus.Malformed)]
    [InlineData("Multikey", null, DidDocumentEntryStatus.Malformed)]
    [InlineData("JsonWebKey2020", "zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", DidDocumentEntryStatus.Malformed)]
    public void TryGetVerificationKey_TellsAMalformedEntryFromAnAbsentOne(string type, string? multibase, DidDocumentEntryStatus expected)
    {
        var key = multibase is null ? "" : ",\"publicKeyMultibase\":\"" + multibase + "\"";
        var document = Parse($$"""
            {"id":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","verificationMethod":[
              {"id":"#atproto_space","type":"{{type}}"{{key}}}
            ]}
            """);

        Assert.Equal(expected, document.TryGetVerificationKey("atproto_space", out var didKey));
        Assert.Equal(expected == DidDocumentEntryStatus.Found ? "did:key:" + multibase : null, didKey);
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetVerificationKey("#atproto", out _));
    }

    [Theory]
    [InlineData("pds.example.com")]
    [InlineData("ftp://pds.example.com")]
    [InlineData("/relative")]
    [InlineData("")]
    public void GetServiceEndpoint_NotAnAbsoluteHttpUrl_IsAbsent(string endpoint)
    {
        var document = Parse($$"""
            {"id":"did:web:example.com","service":[
              {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"{{endpoint}}"}
            ]}
            """);

        Assert.Null(document.GetPdsEndpoint());
    }
}
