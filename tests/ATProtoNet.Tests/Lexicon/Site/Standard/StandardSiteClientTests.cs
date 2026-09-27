using ATProtoNet.Identity;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Lexicon.Site.Standard;

/// <summary>
/// The AT URI overloads' own check. The CRUD calls, each a <c>com.atproto.repo</c> call on its
/// record's collection, are rows in <see cref="EndpointRequestTests"/>.
/// </summary>
public class StandardSiteClientTests
{
    [Theory]
    [InlineData("at://did:plc:author/site.standard.document/self")]
    [InlineData("at://did:plc:author/site.standard.publication")]
    [InlineData("at://did:plc:fan/site.standard.graph.subscription/r1")]
    public async Task GetByAtUri_UriThatNamesNoSuchRecord_ThrowsBeforeSending(string uri)
    {
        using var fixture = new XrpcTestClient();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.Site.GetPublicationAsync(AtUri.Parse(uri)));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.Site.GetRecommendationAsync(AtUri.Parse(uri)));
        Assert.Empty(fixture.Requests);
    }
}
