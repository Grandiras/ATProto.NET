using System.Text.Json;
using ATProtoNet.Blazor.Components;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using Bunit;
using static ATProtoNet.Tests.Auth.SessionKit;
using static ATProtoNet.Tests.Blazor.WidgetHost;

namespace ATProtoNet.Tests.Blazor;

/// <summary><see cref="PostCard"/> renders a post and, without callbacks, likes and reposts it as the signed-in user.</summary>
public sealed class PostCardTests : IAsyncDisposable
{
    private readonly WidgetHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public void Render_ShowsTheAuthorTextAndCounts()
    {
        var cut = _host.Context.Render<PostCard>(p => p.Add(x => x.Post, Post("p1", "hello world", likes: 3)));

        Assert.Equal("Alice", cut.Find(".atproto-post-displayname").TextContent);
        Assert.Equal("@alice.test", cut.Find(".atproto-post-handle").TextContent);
        Assert.Equal("hello world", cut.Find(".atproto-post-text").TextContent);
        Assert.Contains("3", cut.Find(".atproto-post-like").TextContent);
        Assert.Empty(_host.Server.Requests);
    }

    [Theory]
    [InlineData("javascript:alert(document.cookie)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("//evil.example.com/")]
    [InlineData("not a url")]
    public void AnExternalEmbedThatIsNotAWebLink_IsNotRenderedAsALink(string uri)
    {
        var cut = _host.Context.Render<PostCard>(p => p.Add(x => x.Post, WithExternalEmbed(uri)));

        var embed = cut.Find(".atproto-embed-external");
        Assert.Equal("DIV", embed.TagName);
        Assert.Empty(cut.FindAll("a[href]"));
        Assert.Contains("A title", embed.TextContent);
    }

    [Theory]
    [InlineData("https://example.com/article?x=1", "https://example.com/article?x=1")]
    [InlineData("http://example.com", "http://example.com/")]
    public void AnExternalEmbedWithAWebLink_LinksToIt(string uri, string href)
    {
        var cut = _host.Context.Render<PostCard>(p => p.Add(x => x.Post, WithExternalEmbed(uri)));

        var link = cut.Find("a.atproto-embed-external");
        Assert.Equal(href, link.GetAttribute("href"));
        Assert.Equal("_blank", link.GetAttribute("target"));
        Assert.Contains("noopener", link.GetAttribute("rel"));
    }

    private static PostView WithExternalEmbed(string uri)
    {
        var json = System.Text.Json.Nodes.JsonNode.Parse(PostJson("p1", "look"))!.AsObject();
        json["embed"] = new System.Text.Json.Nodes.JsonObject
        {
            ["$type"] = "app.bsky.embed.external#view",
            ["external"] = new System.Text.Json.Nodes.JsonObject
            {
                ["uri"] = uri,
                ["title"] = "A title",
                ["description"] = "A description",
            },
        };
        return JsonSerializer.Deserialize<PostView>(json.ToJsonString(), ATProtoNet.Serialization.AtProtoJsonDefaults.Options)!;
    }

    [Fact]
    public void Like_WithoutACallback_LikesAsTheSignedInUserThenUnlikes()
    {
        _host.Server.Respond = r => r.Nsid switch
        {
            "com.atproto.repo.createRecord" => RecordResponse("app.bsky.feed.like", "3k2la"),
            "com.atproto.repo.deleteRecord" => JsonResponse("{}"),
            _ => throw new InvalidOperationException(r.Nsid),
        };
        var cut = _host.Context.Render<PostCard>(p => p.Add(x => x.Post, Post("p1", "hi", likes: 3)));

        cut.Find(".atproto-post-like").Click();
        cut.WaitForAssertion(() => Assert.Contains("active", cut.Find(".atproto-post-like").ClassName));

        Assert.Contains("4", cut.Find(".atproto-post-like").TextContent);
        using (var body = JsonDocument.Parse(_host.Server.To("com.atproto.repo.createRecord").Single().Body!))
        {
            Assert.Equal(Alice.Value, body.RootElement.GetProperty("repo").GetString());
            Assert.Equal("app.bsky.feed.like", body.RootElement.GetProperty("collection").GetString());
            var subject = body.RootElement.GetProperty("record").GetProperty("subject");
            Assert.Equal($"at://{Alice.Value}/app.bsky.feed.post/p1", subject.GetProperty("uri").GetString());
            Assert.Equal(Cid, subject.GetProperty("cid").GetString());
        }

        cut.Find(".atproto-post-like").Click();
        cut.WaitForAssertion(() => Assert.DoesNotContain("active", cut.Find(".atproto-post-like").ClassName));

        Assert.Contains("3", cut.Find(".atproto-post-like").TextContent);
        using var delete = JsonDocument.Parse(_host.Server.To("com.atproto.repo.deleteRecord").Single().Body!);
        Assert.Equal("3k2la", delete.RootElement.GetProperty("rkey").GetString());
    }

    [Fact]
    public void Repost_WithoutACallback_RepostsAsTheSignedInUser()
    {
        _host.Server.Respond = _ => RecordResponse("app.bsky.feed.repost", "3k2lr");
        var cut = _host.Context.Render<PostCard>(p => p.Add(x => x.Post, Post("p1", "hi")));

        cut.Find(".atproto-post-repost").Click();

        cut.WaitForAssertion(() => Assert.Contains("active", cut.Find(".atproto-post-repost").ClassName));
        using var body = JsonDocument.Parse(_host.Server.To("com.atproto.repo.createRecord").Single().Body!);
        Assert.Equal("app.bsky.feed.repost", body.RootElement.GetProperty("collection").GetString());
    }

    [Fact]
    public void Like_WithACallback_LeavesItToTheCallback()
    {
        PostView? liked = null;
        var post = Post("p1", "hi");
        var cut = _host.Context.Render<PostCard>(p => p
            .Add(x => x.Post, post)
            .Add(x => x.OnLike, (PostView view) => liked = view));

        cut.Find(".atproto-post-like").Click();

        Assert.Same(post, liked);
        Assert.Empty(_host.Server.Requests);
    }

    [Fact]
    public void AnAlreadyLikedPost_IsShownLiked()
    {
        var cut = _host.Context.Render<PostCard>(p => p.Add(
            x => x.Post, Post("p1", "hi", likes: 1, like: $"at://{Alice.Value}/app.bsky.feed.like/3k2la")));

        Assert.Contains("active", cut.Find(".atproto-post-like").ClassName);
        Assert.Equal("true", cut.Find(".atproto-post-like").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void AFailedLike_ShowsAMessageAndKeepsTheState()
    {
        _host.Server.Respond = _ => XrpcError("InvalidRequest");
        var cut = _host.Context.Render<PostCard>(p => p.Add(x => x.Post, Post("p1", "hi", likes: 3)));

        cut.Find(".atproto-post-like").Click();

        cut.WaitForAssertion(() => Assert.Equal("Couldn't update the like.", cut.Find(".atproto-post-error").TextContent));
        Assert.DoesNotContain("active", cut.Find(".atproto-post-like").ClassName);
        Assert.Contains("3", cut.Find(".atproto-post-like").TextContent);
    }

    [Fact]
    public async Task Like_SignedOut_AsksToSignIn()
    {
        await using var host = new WidgetHost(signedIn: false);
        var cut = host.Context.Render<PostCard>(p => p.Add(x => x.Post, Post("p1", "hi")));

        cut.Find(".atproto-post-like").Click();

        cut.WaitForAssertion(() => Assert.Equal("Sign in to do that.", cut.Find(".atproto-post-error").TextContent));
        Assert.Empty(host.Server.Requests);
    }
}
