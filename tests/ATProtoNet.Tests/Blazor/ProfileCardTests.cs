using System.Text.Json;
using ATProtoNet.Blazor.Components;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Serialization;
using Bunit;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Blazor;

/// <summary><see cref="ProfileCard"/> shows a given profile, or fetches one by actor as the signed-in user.</summary>
public sealed class ProfileCardTests : IAsyncDisposable
{
    private const string ProfileJson =
        """{"did":"did:plc:bob","handle":"bob.test","displayName":"Bob","description":"Hi, I am Bob","followersCount":7,"followsCount":2,"postsCount":40}""";

    private readonly WidgetHost _host = new();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public void AGivenProfile_IsShownWithoutFetching()
    {
        var profile = JsonSerializer.Deserialize<ProfileViewDetailed>(ProfileJson, AtProtoJsonDefaults.Options)!;

        var cut = _host.Context.Render<ProfileCard>(p => p
            .Add(x => x.Profile, profile)
            .Add(x => x.Actor, AtIdentifier.Parse("someone.else")));

        Assert.Equal("Bob", cut.Find(".atproto-profile-displayname").TextContent);
        Assert.Contains("7", cut.Find(".atproto-profile-stats").TextContent);
        Assert.Empty(_host.Server.Requests);
    }

    [Fact]
    public void AnActor_IsFetchedAsTheSignedInUser()
    {
        _host.Server.Respond = r => r.Nsid == "app.bsky.actor.getProfile"
            ? JsonResponse(ProfileJson)
            : throw new InvalidOperationException(r.Nsid);

        var cut = _host.Context.Render<ProfileCard>(p => p.Add(x => x.Actor, AtIdentifier.Parse("bob.test")));

        cut.WaitForAssertion(() => Assert.Equal("Bob", cut.Find(".atproto-profile-displayname").TextContent));
        Assert.Equal("Hi, I am Bob", cut.Find(".atproto-profile-description").TextContent);
        var request = Assert.Single(_host.Server.Requests);
        Assert.Equal("bob.test", System.Web.HttpUtility.ParseQueryString(request.Uri.Query)["actor"]);
        Assert.StartsWith("Bearer ", request.Authorization);
    }

    [Fact]
    public void AFailure_ShowsAMessageButNotTheException()
    {
        _host.Server.Respond = _ => XrpcError("AccountTakedown");

        var cut = _host.Context.Render<ProfileCard>(p => p.Add(x => x.Actor, AtIdentifier.Parse("bob.test")));

        cut.WaitForAssertion(() => Assert.Equal("Couldn't load the profile.", cut.Find(".atproto-profile-error").TextContent.Trim()));
        Assert.DoesNotContain("AccountTakedown", cut.Markup);
    }

    [Fact]
    public async Task AnActor_SignedOut_AsksToSignIn()
    {
        await using var host = new WidgetHost(signedIn: false);

        var cut = host.Context.Render<ProfileCard>(p => p.Add(x => x.Actor, AtIdentifier.Parse("bob.test")));

        cut.WaitForAssertion(() => Assert.Equal("Sign in to see this profile.", cut.Find(".atproto-profile-error").TextContent.Trim()));
        Assert.Empty(host.Server.Requests);
    }

    [Fact]
    public void NeitherProfileNorActor_RendersNothing()
    {
        var cut = _host.Context.Render<ProfileCard>();

        Assert.Equal("", cut.Markup.Trim());
    }
}
