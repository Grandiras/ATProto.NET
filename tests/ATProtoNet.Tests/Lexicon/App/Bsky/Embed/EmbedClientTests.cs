using ATProtoNet.Identity;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Embed;

public sealed class EmbedClientTests : IDisposable
{
    private const string DocumentUri = $"at://{AliceDid}/site.standard.document/3lwinfmsd2k2i";
    private const string PublicationUri = $"at://{AliceDid}/site.standard.publication/3lwinfmsd2k2j";

    private readonly ScriptedXrpcHandler _handler = new();
    private readonly AtProtoClient _client;

    public EmbedClientTests() => _client = _handler.CreateClient();

    public void Dispose()
    {
        _client.Dispose();
        _handler.Dispose();
    }

    [Fact]
    public async Task GetEmbedExternalViewAsync_SendsUrlAndUris_BindsTheView()
    {
        _handler.On("app.bsky.embed.getEmbedExternalView", $$$"""
            {"view":{"$type":"app.bsky.embed.external#view","external":{"uri":"https://alice.example.com/post","title":"A post","description":"About things","createdAt":"2026-09-01T00:00:00.000Z","readingTime":4,
               "source":{"uri":"https://alice.example.com","title":"Alice's blog","theme":{"accentRGB":{"r":10,"g":20,"b":30} } },
               "associatedRefs":[{"uri":"{{{DocumentUri}}}","cid":"{{{PostCid}}}"}],"associatedProfiles":[{{{AliceBasicJson}}}]}},
             "associatedRefs":[{"uri":"{{{DocumentUri}}}","cid":"{{{PostCid}}}"},{"uri":"{{{PublicationUri}}}","cid":"{{{OtherCid}}}"}],
             "associatedRecords":[{"$type":"site.standard.document","title":"A post"},{"$type":"site.standard.publication","name":"Alice's blog"}]}
            """);

        var response = await _client.Bsky.Embed.GetEmbedExternalViewAsync(
            "https://alice.example.com/post", [AtUri.Parse(DocumentUri), AtUri.Parse(PublicationUri)]);

        var request = Assert.Single(_handler.Requests);
        var query = request.Parameters;
        Assert.Equal("https://alice.example.com/post", query["url"]);
        Assert.Equal([DocumentUri, PublicationUri], request.ValuesOf("uris"));

        var external = response.View!.External;
        Assert.Equal("A post", external.Title);
        Assert.Equal(4, external.ReadingTime);
        Assert.Equal(30, external.Source!.Theme!.AccentRgb!.B);
        Assert.Equal("alice.test", Assert.Single(external.AssociatedProfiles!).Handle.Value);
        Assert.Equal([DocumentUri, PublicationUri], response.AssociatedRefs!.Select(r => r.Uri.Value));
        Assert.Equal("site.standard.publication", response.AssociatedRecords![1].GetProperty("$type").GetString());
    }

    [Fact]
    public async Task GetEmbedExternalViewAsync_NothingResolved_IsEmpty()
    {
        _handler.On("app.bsky.embed.getEmbedExternalView", "{}");

        var response = await _client.Bsky.Embed.GetEmbedExternalViewAsync(
            "https://example.com", [AtUri.Parse(DocumentUri)]);

        Assert.Null(response.View);
        Assert.Null(response.AssociatedRefs);
        Assert.Null(response.AssociatedRecords);
    }
}
