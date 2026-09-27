using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

public class AtProtoScopesTests
{
    // ─── Transitional scope constants ───────────────────────────────────

    public static TheoryData<string, string, string> ScopeConstants() => new()
    {
        { nameof(AtProtoScopes.Default), AtProtoScopes.Default, "atproto transition:generic" },
        { nameof(AtProtoScopes.WithChat), AtProtoScopes.WithChat, "atproto transition:generic transition:chat.bsky" },
        { nameof(AtProtoScopes.AuthOnly), AtProtoScopes.AuthOnly, "atproto" },
        { nameof(AtProtoScopes.AtProto), AtProtoScopes.AtProto, "atproto" },
        { nameof(AtProtoScopes.TransitionGeneric), AtProtoScopes.TransitionGeneric, "transition:generic" },
        { nameof(AtProtoScopes.TransitionChatBsky), AtProtoScopes.TransitionChatBsky, "transition:chat.bsky" },
        { nameof(AtProtoScopes.TransitionEmail), AtProtoScopes.TransitionEmail, "transition:email" },
    };

    [Theory]
    [MemberData(nameof(ScopeConstants))]
    public void ScopeConstants_MatchExpectedValues(string name, string actual, string expected) =>
        Assert.True(expected == actual, $"{name}: expected '{expected}' but was '{actual}'");

    // ─── Repo scopes ────────────────────────────────────────────────────

    public static TheoryData<Func<string>, string> RepoScopeCases() => new()
    {
        { () => AtProtoScopes.Repo("app.bsky.feed.post"), "repo:app.bsky.feed.post" },
        {
            () => AtProtoScopes.Repo("app.bsky.feed.post", RepoAction.Create | RepoAction.Delete),
            "repo:app.bsky.feed.post?action=create&action=delete"
        },
        { () => AtProtoScopes.Repo("app.bsky.feed.like", RepoAction.Delete), "repo:app.bsky.feed.like?action=delete" },
        { () => AtProtoScopes.Repo("*"), "repo:*" },
        { () => AtProtoScopes.Repo("*", RepoAction.Delete), "repo:*?action=delete" },
        {
            () => AtProtoScopes.Repo(["app.bsky.feed.post", "app.bsky.feed.like"]),
            "repo?collection=app.bsky.feed.post&collection=app.bsky.feed.like"
        },
        {
            () => AtProtoScopes.Repo(["app.bsky.feed.post", "app.bsky.feed.like"], RepoAction.Create | RepoAction.Delete),
            "repo?collection=app.bsky.feed.post&collection=app.bsky.feed.like&action=create&action=delete"
        },
        {
            // The list overload delegates to the single-collection one for a one-element list.
            () => AtProtoScopes.Repo(["app.bsky.feed.post"]), "repo:app.bsky.feed.post"
        },
    };

    [Theory]
    [MemberData(nameof(RepoScopeCases))]
    public void Repo_ProducesTheExpectedScope(Func<string> build, string expected) => Assert.Equal(expected, build());

    [Fact]
    public void Repo_EmptyCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Repo([]));
    }

    public static TheoryData<Action> RepoNoActionsCases() => new()
    {
        () => AtProtoScopes.Repo("app.bsky.feed.post", RepoAction.None),
        () => AtProtoScopes.Repo(["app.bsky.feed.post", "app.bsky.feed.like"], RepoAction.None),
        // The list overload delegates to the single-collection one for a one-element list, so
        // the guard has to hold on that path too.
        () => AtProtoScopes.Repo(["app.bsky.feed.post"], RepoAction.None),
    };

    [Theory]
    [MemberData(nameof(RepoNoActionsCases))]
    public void Repo_NoActions_IsRejectedRatherThanSilentlyWidened(Action call)
    {
        // An omitted action list means RepoAction.All, so quietly emitting nothing for None
        // would hand back a create/update/delete grant instead of the zero-write one asked
        // for. The grammar cannot express it, so the call fails instead.
        var ex = Assert.Throws<ArgumentException>(call);

        Assert.Equal("actions", ex.ParamName);
    }

    // ─── Rpc scopes ─────────────────────────────────────────────────────

    public static TheoryData<Func<string>, string> RpcScopeCases() => new()
    {
        {
            () => AtProtoScopes.Rpc("app.bsky.feed.searchPosts", "did:web:api.bsky.app#bsky_appview"),
            "rpc:app.bsky.feed.searchPosts?aud=did:web:api.bsky.app%23bsky_appview"
        },
        {
            () => AtProtoScopes.Rpc("*", "did:web:api.bsky.app#bsky_appview"),
            "rpc:*?aud=did:web:api.bsky.app%23bsky_appview"
        },
        { () => AtProtoScopes.Rpc("com.atproto.moderation.createReport", "*"), "rpc:com.atproto.moderation.createReport?aud=*" },
        {
            () => AtProtoScopes.Rpc(["app.bsky.feed.searchPosts", "app.bsky.feed.getTimeline"], "did:web:api.bsky.app#bsky_appview"),
            "rpc?lxm=app.bsky.feed.searchPosts&lxm=app.bsky.feed.getTimeline&aud=did:web:api.bsky.app%23bsky_appview"
        },
        { () => AtProtoScopes.Rpc(["app.bsky.feed.searchPosts"], "*"), "rpc:app.bsky.feed.searchPosts?aud=*" },
    };

    [Theory]
    [MemberData(nameof(RpcScopeCases))]
    public void Rpc_ProducesTheExpectedScope(Func<string> build, string expected) => Assert.Equal(expected, build());

    [Fact]
    public void Rpc_BothWildcards_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Rpc("*", "*"));
    }

    // ─── Blob scopes ────────────────────────────────────────────────────

    public static TheoryData<Func<string>, string> BlobScopeCases() => new()
    {
        { () => AtProtoScopes.Blob(), "blob:*/*" },
        { () => AtProtoScopes.Blob("video/*"), "blob:video/*" },
        { () => AtProtoScopes.Blob(["video/*", "text/html"]), "blob?accept=video/*&accept=text/html" },
        { () => AtProtoScopes.Blob(["image/png"]), "blob:image/png" },
    };

    [Theory]
    [MemberData(nameof(BlobScopeCases))]
    public void Blob_ProducesTheExpectedScope(Func<string> build, string expected) => Assert.Equal(expected, build());

    // ─── Account scopes ─────────────────────────────────────────────────

    public static TheoryData<Func<string>, string> AccountScopeCases() => new()
    {
        { () => AtProtoScopes.Account("email"), "account:email" },
        { () => AtProtoScopes.Account("repo", AccountAction.Manage), "account:repo?action=manage" },
        { () => AtProtoScopes.Account("status"), "account:status" },
    };

    [Theory]
    [MemberData(nameof(AccountScopeCases))]
    public void Account_ProducesTheExpectedScope(Func<string> build, string expected) => Assert.Equal(expected, build());

    // ─── Identity scopes ────────────────────────────────────────────────

    [Fact]
    public void Identity_ManageHandle()
    {
        Assert.Equal("identity:handle", AtProtoScopes.Identity("handle"));
    }

    [Fact]
    public void Identity_FullControl()
    {
        Assert.Equal("identity:*", AtProtoScopes.Identity("*"));
    }

    // ─── Audiences ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("did:web:api.bsky.app#bsky_appview", "did:web:api.bsky.app%23bsky_appview")]
    [InlineData("did:plc:ewvi7nxzyoun6zhxrhs64oiz#atproto_labeler", "did:plc:ewvi7nxzyoun6zhxrhs64oiz%23atproto_labeler")]
    [InlineData("did:web:api.bsky.app%23bsky_appview", "did:web:api.bsky.app%23bsky_appview")]
    [InlineData("*", "*")]
    public void Rpc_ServiceReferenceOrWildcard_IsAccepted(string aud, string encoded)
    {
        Assert.Equal($"rpc:app.bsky.feed.getTimeline?aud={encoded}", AtProtoScopes.Rpc("app.bsky.feed.getTimeline", aud));
    }

    [Theory]
    [InlineData("did:web:api.bsky.app")]               // a bare DID: the reference parser rejects it
    [InlineData("did:web:api.bsky.app#")]              // an empty fragment
    [InlineData("#bsky_appview")]                      // no DID
    [InlineData("api.bsky.app#bsky_appview")]          // not a DID
    [InlineData("did:web:api.bsky.app#a&aud=*")]       // scope syntax smuggled in the fragment
    [InlineData("did:web:api.bsky.app#a b")]
    public void Rpc_AudThatIsNotAServiceReference_Throws(string aud)
    {
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Rpc("app.bsky.feed.getTimeline", aud));
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Rpc(["app.bsky.feed.getTimeline", "app.bsky.feed.getPosts"], aud));
    }

    [Fact]
    public void Include_WildcardAud_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Include(AtProtoScopes.PermissionSets.FullApp, "*"));
    }

    [Fact]
    public void Include_BareDidAud_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Include(AtProtoScopes.PermissionSets.FullApp, "did:web:api.bsky.app"));
    }

    // ─── Presets ────────────────────────────────────────────────────────

    [Fact]
    public void Presets_AreBuiltFromThePublishedPermissionSets()
    {
        Assert.Equal(
            AtProtoScopes.Presets.BlueskyApp,
            AtProtoScopes.Combine(
                AtProtoScopes.AtProto,
                AtProtoScopes.Include(AtProtoScopes.PermissionSets.FullApp, AtProtoScopes.BlueskyAppView),
                AtProtoScopes.Blob()));

        Assert.Equal(
            AtProtoScopes.Presets.BlueskyAppWithChat,
            AtProtoScopes.Combine(
                AtProtoScopes.Presets.BlueskyApp,
                AtProtoScopes.Include(AtProtoScopes.PermissionSets.FullChatClient, AtProtoScopes.BlueskyChat)));

        Assert.Equal(
            AtProtoScopes.Presets.BlueskyReadOnly,
            AtProtoScopes.Combine(
                AtProtoScopes.AtProto,
                AtProtoScopes.Include(AtProtoScopes.PermissionSets.ViewAll, AtProtoScopes.BlueskyAppView)));

        Assert.Equal(
            AtProtoScopes.Presets.BlueskyPosting,
            AtProtoScopes.Combine(
                AtProtoScopes.AtProto,
                AtProtoScopes.Include(AtProtoScopes.PermissionSets.CreatePosts, AtProtoScopes.BlueskyAppView),
                AtProtoScopes.Blob()));
    }

    [Fact]
    public void Presets_UseNoTransitionalScope()
    {
        foreach (var preset in new[]
                 {
                     AtProtoScopes.Presets.BlueskyApp, AtProtoScopes.Presets.BlueskyAppWithChat,
                     AtProtoScopes.Presets.BlueskyReadOnly, AtProtoScopes.Presets.BlueskyPosting,
                 })
        {
            var scopes = preset.Split(' ');
            Assert.Equal(AtProtoScopes.AtProto, scopes[0]);
            Assert.DoesNotContain(scopes, scope => scope.StartsWith("transition:", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ServiceAudiences_MatchTheServiceProxyConstants()
    {
        Assert.Equal(ATProtoNet.Http.ServiceProxy.BskyAppViewHeader, AtProtoScopes.BlueskyAppView);
        Assert.Equal(ATProtoNet.Http.ServiceProxy.BskyChatHeader, AtProtoScopes.BlueskyChat);
    }

    // ─── Include (permission sets) ──────────────────────────────────────

    [Fact]
    public void Include_WithoutAud()
    {
        Assert.Equal(
            "include:app.bsky.authBasicFeatures",
            AtProtoScopes.Include("app.bsky.authBasicFeatures"));
    }

    [Fact]
    public void Include_WithAud()
    {
        Assert.Equal(
            "include:app.bsky.authBasicFeatures?aud=did:web:api.bsky.app%23svc_appview",
            AtProtoScopes.Include("app.bsky.authBasicFeatures", "did:web:api.bsky.app#svc_appview"));
    }

    // ─── Combine ────────────────────────────────────────────────────────

    [Fact]
    public void Combine_MergesAndDeduplicates()
    {
        var result = AtProtoScopes.Combine(
            AtProtoScopes.Default,
            AtProtoScopes.TransitionChatBsky,
            AtProtoScopes.TransitionGeneric); // already in Default

        var parts = result.Split(' ');
        Assert.Equal(3, parts.Length);
        Assert.Contains("atproto", parts);
        Assert.Contains("transition:generic", parts);
        Assert.Contains("transition:chat.bsky", parts);
    }

    [Fact]
    public void Combine_HandlesEmptyInput()
    {
        var result = AtProtoScopes.Combine();
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Combine_HandlesSingleScope()
    {
        var result = AtProtoScopes.Combine(AtProtoScopes.AtProto);
        Assert.Equal("atproto", result);
    }

    [Fact]
    public void Combine_GranularScopes()
    {
        var result = AtProtoScopes.Combine(
            AtProtoScopes.AtProto,
            AtProtoScopes.Repo("app.bsky.feed.post"),
            AtProtoScopes.Rpc("app.bsky.feed.searchPosts", "*"),
            AtProtoScopes.Blob());

        var parts = result.Split(' ');
        Assert.Equal(4, parts.Length);
        Assert.Contains("atproto", parts);
        Assert.Contains("repo:app.bsky.feed.post", parts);
        Assert.Contains("rpc:app.bsky.feed.searchPosts?aud=*", parts);
        Assert.Contains("blob:*/*", parts);
    }
}
