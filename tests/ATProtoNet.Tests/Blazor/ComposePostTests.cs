using System.Text.Json;
using ATProtoNet.Blazor.Components;
using Bunit;
using static ATProtoNet.Tests.Auth.SessionKit;
using static ATProtoNet.Tests.Blazor.WidgetHost;

namespace ATProtoNet.Tests.Blazor;

/// <summary><see cref="ComposePost"/> posts as the signed-in user, within the 300-grapheme limit.</summary>
public sealed class ComposePostTests : IAsyncDisposable
{
    private readonly WidgetHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public void Submit_PostsAsTheSignedInUserAndReportsTheRecord()
    {
        _host.Server.Respond = r => r.Nsid == "com.atproto.repo.createRecord"
            ? RecordResponse("app.bsky.feed.post", "3k2lp")
            : throw new InvalidOperationException(r.Nsid);
        RecordRef? created = null;
        var cut = _host.Context.Render<ComposePost>(p => p.Add(x => x.OnPostCreated, (RecordRef post) => created = post));

        cut.Find("textarea").Input("Hello from Blazor");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.NotNull(created));
        Assert.Equal("3k2lp", created!.RecordKey.Value);
        using var body = JsonDocument.Parse(_host.Server.To("com.atproto.repo.createRecord").Single().Body!);
        Assert.Equal(Alice.Value, body.RootElement.GetProperty("repo").GetString());
        Assert.Equal("app.bsky.feed.post", body.RootElement.GetProperty("collection").GetString());
        Assert.Equal("Hello from Blazor", body.RootElement.GetProperty("record").GetProperty("text").GetString());
        Assert.Equal("", cut.Find("textarea").GetAttribute("value") ?? "");
    }

    [Fact]
    public void TheCounter_CountsGraphemes()
    {
        var cut = _host.Context.Render<ComposePost>();

        // A family emoji is one grapheme of eight UTF-16 code units.
        cut.Find("textarea").Input("hi 👨‍👩‍👧");

        Assert.Equal("4 / 300", cut.Find(".atproto-compose-charcount").TextContent.Trim());
        Assert.False(cut.Find("button[type=submit]").HasAttribute("disabled"));
    }

    [Fact]
    public void OverTheLimit_CannotBePosted()
    {
        var cut = _host.Context.Render<ComposePost>();

        cut.Find("textarea").Input(new string('a', 301));

        Assert.Contains("over", cut.Find(".atproto-compose-charcount").ClassName);
        Assert.True(cut.Find("button[type=submit]").HasAttribute("disabled"));
        cut.Find("form").Submit();
        Assert.Empty(_host.Server.Requests);
    }

    [Fact]
    public void AFailure_ShowsAMessageAndKeepsTheText()
    {
        _host.Server.Respond = _ => XrpcError("InvalidRequest");
        var cut = _host.Context.Render<ComposePost>();

        cut.Find("textarea").Input("keep me");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Couldn't create the post. Try again.", cut.Find(".atproto-compose-error").TextContent));
        Assert.Equal("keep me", cut.Find("textarea").GetAttribute("value"));
    }

    [Fact]
    public async Task SignedOut_AsksToSignIn()
    {
        await using var host = new WidgetHost(signedIn: false);
        var cut = host.Context.Render<ComposePost>();

        cut.Find("textarea").Input("hello");
        cut.Find("form").Submit();

        cut.WaitForAssertion(() => Assert.Equal("Sign in to post.", cut.Find(".atproto-compose-error").TextContent));
        Assert.Empty(host.Server.Requests);
    }
}
