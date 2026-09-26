using ATProtoNet.Blazor;
using ATProtoNet.Blazor.Components;
using ATProtoNet.Identity;
using Bunit;
using static ATProtoNet.Tests.Auth.SessionKit;
using static ATProtoNet.Tests.Blazor.WidgetHost;

namespace ATProtoNet.Tests.Blazor;

/// <summary><see cref="FeedView"/> reads its feed as the signed-in user.</summary>
public sealed class FeedViewTests : IAsyncDisposable
{
    private readonly WidgetHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public void Timeline_IsReadAsTheSignedInUser()
    {
        _host.Server.Respond = r => r.Nsid == "app.bsky.feed.getTimeline"
            ? FeedResponse(null, PostJson("p1", "first post"), PostJson("p2", "second post"))
            : throw new InvalidOperationException(r.Nsid);

        var cut = _host.Context.Render<FeedView>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("article.atproto-post").Count));
        Assert.Contains("first post", cut.Markup);
        var request = Assert.Single(_host.Server.Requests);
        Assert.StartsWith("Bearer ", request.Authorization);
        Assert.Equal("25", System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["limit"]);
    }

    [Fact]
    public void AnAuthorSource_ReadsThatAccountsFeed()
    {
        _host.Server.Respond = _ => FeedResponse(null, PostJson("p1", "by bob"));

        var cut = _host.Context.Render<FeedView>(p => p
            .Add(x => x.FeedSource, FeedSource.Author(AtIdentifier.Parse("bob.test")))
            .Add(x => x.PageSize, 10));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("article.atproto-post")));
        var request = Assert.Single(_host.Server.Requests);
        Assert.Equal("app.bsky.feed.getAuthorFeed", request.Nsid);
        var query = System.Web.HttpUtility.ParseQueryString(request.Uri.Query);
        Assert.Equal("bob.test", query["actor"]);
        Assert.Equal("10", query["limit"]);
    }

    [Theory]
    [InlineData("feed", "app.bsky.feed.getFeed", "feed")]
    [InlineData("list", "app.bsky.feed.getListFeed", "list")]
    public void GeneratorAndListSources_ReadTheirFeeds(string kind, string nsid, string parameter)
    {
        var uri = AtUri.Parse("at://did:plc:bob/app.bsky.feed.generator/cats");
        _host.Server.Respond = _ => FeedResponse(null, PostJson("p1", "cats"));

        var cut = _host.Context.Render<FeedView>(p => p
            .Add(x => x.FeedSource, kind == "feed" ? FeedSource.Feed(uri) : FeedSource.List(uri)));

        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("article.atproto-post")));
        var request = Assert.Single(_host.Server.Requests);
        Assert.Equal(nsid, request.Nsid);
        Assert.Equal(uri.Value, System.Web.HttpUtility.ParseQueryString(request.Uri.Query)[parameter]);
    }

    [Fact]
    public void OnePostTwiceInTheFeed_RendersBoth()
    {
        // A timeline holds a post once for each account that reposted it.
        _host.Server.Respond = _ => FeedResponse(null, PostJson("p1", "twice"), PostJson("p1", "twice"));

        var cut = _host.Context.Render<FeedView>();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("article.atproto-post").Count));
    }

    [Fact]
    public void LoadMore_ContinuesFromTheCursor()
    {
        _host.Server.Respond = r => r.Uri.Query.Contains("cursor=c1")
            ? FeedResponse(null, PostJson("p2", "older"))
            : FeedResponse("c1", PostJson("p1", "newer"));
        var cut = _host.Context.Render<FeedView>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("article.atproto-post")));

        cut.Find(".atproto-feed-load-more button").Click();

        cut.WaitForAssertion(() => Assert.Equal(2, cut.FindAll("article.atproto-post").Count));
        Assert.Empty(cut.FindAll(".atproto-feed-load-more"));
    }

    [Fact]
    public void ARerenderWithTheSameSource_DoesNotFetchAgain()
    {
        _host.Server.Respond = _ => FeedResponse(null, PostJson("p1", "once"));
        var cut = _host.Context.Render<FeedView>();
        cut.WaitForAssertion(() => Assert.Single(cut.FindAll("article.atproto-post")));

        cut.Render(p => p.Add(x => x.FeedSource, FeedSource.Timeline).Add(x => x.CssClass, "changed"));
        cut.Render(p => p.Add(x => x.FeedSource, FeedSource.Author(AtIdentifier.Parse("bob.test"))));

        cut.WaitForAssertion(() => Assert.Equal(2, _host.Server.Requests.Count));
        Assert.Equal("app.bsky.feed.getAuthorFeed", _host.Server.Requests[1].Nsid);
    }

    [Fact]
    public void AFailure_ShowsAMessageButNotTheException()
    {
        _host.Server.Respond = _ => XrpcError("InternalServerError", System.Net.HttpStatusCode.BadRequest);

        var cut = _host.Context.Render<FeedView>();

        cut.WaitForAssertion(() => Assert.Contains("Couldn't load the feed.", cut.Markup));
        Assert.DoesNotContain("InternalServerError", cut.Markup);
        Assert.NotNull(cut.Find(".atproto-feed-error button"));
    }

    [Fact]
    public async Task SignedOut_AsksToSignInAndFetchesNothing()
    {
        await using var host = new WidgetHost(signedIn: false);

        var cut = host.Context.Render<FeedView>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".atproto-feed-signed-out")));
        Assert.Empty(host.Server.Requests);
    }

    [Fact]
    public async Task SignedInWithoutAStoredSession_AsksToSignIn()
    {
        await _host.Store.RemoveAsync(Alice);

        var cut = _host.Context.Render<FeedView>();

        cut.WaitForAssertion(() => Assert.NotNull(cut.Find(".atproto-feed-signed-out")));
    }
}
