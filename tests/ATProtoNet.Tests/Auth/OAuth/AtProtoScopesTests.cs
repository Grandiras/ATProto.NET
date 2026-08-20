using ATProtoNet.Auth.OAuth;

namespace ATProtoNet.Tests.Auth.OAuth;

public class AtProtoScopesTests
{
    // ─── Transitional scope constants ───────────────────────────────────

    [Fact]
    public void Default_ContainsAtProtoAndTransitionGeneric()
    {
        Assert.Equal("atproto transition:generic", AtProtoScopes.Default);
    }

    [Fact]
    public void WithChat_ContainsChatScope()
    {
        Assert.Equal("atproto transition:generic transition:chat.bsky", AtProtoScopes.WithChat);
    }

    [Fact]
    public void AuthOnly_IsJustAtProto()
    {
        Assert.Equal("atproto", AtProtoScopes.AuthOnly);
    }

    [Fact]
    public void Constants_MatchExpectedValues()
    {
        Assert.Equal("atproto", AtProtoScopes.AtProto);
        Assert.Equal("transition:generic", AtProtoScopes.TransitionGeneric);
        Assert.Equal("transition:chat.bsky", AtProtoScopes.TransitionChatBsky);
        Assert.Equal("transition:email", AtProtoScopes.TransitionEmail);
    }

    // ─── Repo scopes ────────────────────────────────────────────────────

    [Fact]
    public void Repo_SingleCollection_AllActions()
    {
        Assert.Equal("repo:app.bsky.feed.post", AtProtoScopes.Repo("app.bsky.feed.post"));
    }

    [Fact]
    public void Repo_SingleCollection_SpecificActions()
    {
        Assert.Equal(
            "repo:app.bsky.feed.post?action=create&action=delete",
            AtProtoScopes.Repo("app.bsky.feed.post", RepoAction.Create | RepoAction.Delete));
    }

    [Fact]
    public void Repo_SingleCollection_SingleAction()
    {
        Assert.Equal(
            "repo:app.bsky.feed.like?action=delete",
            AtProtoScopes.Repo("app.bsky.feed.like", RepoAction.Delete));
    }

    [Fact]
    public void Repo_Wildcard()
    {
        Assert.Equal("repo:*", AtProtoScopes.Repo("*"));
    }

    [Fact]
    public void Repo_Wildcard_WithActions()
    {
        Assert.Equal("repo:*?action=delete", AtProtoScopes.Repo("*", RepoAction.Delete));
    }

    [Fact]
    public void Repo_MultipleCollections()
    {
        Assert.Equal(
            "repo?collection=app.bsky.feed.post&collection=app.bsky.feed.like",
            AtProtoScopes.Repo(["app.bsky.feed.post", "app.bsky.feed.like"]));
    }

    [Fact]
    public void Repo_MultipleCollections_WithActions()
    {
        Assert.Equal(
            "repo?collection=app.bsky.feed.post&collection=app.bsky.feed.like&action=create&action=delete",
            AtProtoScopes.Repo(["app.bsky.feed.post", "app.bsky.feed.like"], RepoAction.Create | RepoAction.Delete));
    }

    [Fact]
    public void Repo_SingleCollectionList_DelegatesToSingleOverload()
    {
        Assert.Equal("repo:app.bsky.feed.post", AtProtoScopes.Repo(["app.bsky.feed.post"]));
    }

    [Fact]
    public void Repo_EmptyCollection_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Repo([]));
    }

    [Fact]
    public void Repo_NoActions_IsRejectedRatherThanSilentlyWidened()
    {
        // An omitted action list means RepoAction.All, so quietly emitting nothing for None
        // would hand back a create/update/delete grant instead of the zero-write one asked
        // for. The grammar cannot express it, so the call fails instead.
        var ex = Assert.Throws<ArgumentException>(
            () => AtProtoScopes.Repo("app.bsky.feed.post", RepoAction.None));

        Assert.Equal("actions", ex.ParamName);
    }

    [Fact]
    public void Repo_MultipleCollections_NoActions_IsRejected()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => AtProtoScopes.Repo(["app.bsky.feed.post", "app.bsky.feed.like"], RepoAction.None));

        Assert.Equal("actions", ex.ParamName);
    }

    [Fact]
    public void Repo_SingleCollectionList_NoActions_IsRejected()
    {
        // The list overload delegates to the single-collection one for a one-element list, so
        // the guard has to hold on that path too.
        var ex = Assert.Throws<ArgumentException>(
            () => AtProtoScopes.Repo(["app.bsky.feed.post"], RepoAction.None));

        Assert.Equal("actions", ex.ParamName);
    }

    // ─── Rpc scopes ─────────────────────────────────────────────────────

    [Fact]
    public void Rpc_SingleLxm_WithAud()
    {
        Assert.Equal(
            "rpc:app.bsky.feed.searchPosts?aud=did:web:api.bsky.app%23bsky_appview",
            AtProtoScopes.Rpc("app.bsky.feed.searchPosts", "did:web:api.bsky.app#bsky_appview"));
    }

    [Fact]
    public void Rpc_WildcardLxm()
    {
        Assert.Equal(
            "rpc:*?aud=did:web:api.bsky.app%23bsky_appview",
            AtProtoScopes.Rpc("*", "did:web:api.bsky.app#bsky_appview"));
    }

    [Fact]
    public void Rpc_WildcardAud()
    {
        Assert.Equal(
            "rpc:com.atproto.moderation.createReport?aud=*",
            AtProtoScopes.Rpc("com.atproto.moderation.createReport", "*"));
    }

    [Fact]
    public void Rpc_BothWildcards_Throws()
    {
        Assert.Throws<ArgumentException>(() => AtProtoScopes.Rpc("*", "*"));
    }

    [Fact]
    public void Rpc_MultipleLxms()
    {
        Assert.Equal(
            "rpc?lxm=app.bsky.feed.searchPosts&lxm=app.bsky.feed.getTimeline&aud=did:web:api.bsky.app%23bsky_appview",
            AtProtoScopes.Rpc(["app.bsky.feed.searchPosts", "app.bsky.feed.getTimeline"], "did:web:api.bsky.app#bsky_appview"));
    }

    [Fact]
    public void Rpc_SingleLxmList_DelegatesToSingleOverload()
    {
        Assert.Equal(
            "rpc:app.bsky.feed.searchPosts?aud=*",
            AtProtoScopes.Rpc(["app.bsky.feed.searchPosts"], "*"));
    }

    // ─── Blob scopes ────────────────────────────────────────────────────

    [Fact]
    public void Blob_AllTypes()
    {
        Assert.Equal("blob:*/*", AtProtoScopes.Blob());
    }

    [Fact]
    public void Blob_SpecificType()
    {
        Assert.Equal("blob:video/*", AtProtoScopes.Blob("video/*"));
    }

    [Fact]
    public void Blob_MultipleTypes()
    {
        Assert.Equal(
            "blob?accept=video/*&accept=text/html",
            AtProtoScopes.Blob(["video/*", "text/html"]));
    }

    [Fact]
    public void Blob_SingleTypeList_DelegatesToSingleOverload()
    {
        Assert.Equal("blob:image/png", AtProtoScopes.Blob(["image/png"]));
    }

    // ─── Account scopes ─────────────────────────────────────────────────

    [Fact]
    public void Account_ReadEmail()
    {
        Assert.Equal("account:email", AtProtoScopes.Account("email"));
    }

    [Fact]
    public void Account_ManageRepo()
    {
        Assert.Equal("account:repo?action=manage", AtProtoScopes.Account("repo", AccountAction.Manage));
    }

    [Fact]
    public void Account_ReadStatus()
    {
        Assert.Equal("account:status", AtProtoScopes.Account("status"));
    }

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

    [Fact]
    public void Identity_SubmitAction()
    {
        Assert.Equal("identity:handle?action=submit", AtProtoScopes.Identity("handle", IdentityAction.Submit));
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
