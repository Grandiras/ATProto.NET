using System.Text.Json;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.Spaces;

public class SpaceScopeTests
{
    [Fact]
    public void Space_BareGrant_OmitsEveryDefault()
    {
        // authority defaults to self, skey to *, collection to the type's declared list, and
        // action to read+create+update+delete — so the personal-data grant is just the type.
        Assert.Equal("space:com.example.bookmarks", AtProtoScopes.Space("com.example.bookmarks"));
    }

    [Fact]
    public void Space_ExplicitSelfAuthorityAndWildcardKey_AreStillOmitted()
    {
        Assert.Equal(
            "space:com.example.bookmarks",
            AtProtoScopes.Space("com.example.bookmarks", authority: "self", skey: "*"));
    }

    [Fact]
    public void Space_WildcardAuthority_ReachesSpacesAnchoredElsewhere()
    {
        Assert.Equal(
            "space:com.atmoboards.forum?authority=*",
            AtProtoScopes.Space("com.atmoboards.forum", authority: "*"));
    }

    [Fact]
    public void Space_ReadOnly_NeedsNoCollections()
    {
        // Read access is all-or-nothing at the space boundary, so it ignores the collection list.
        Assert.Equal(
            "space:com.atmoboards.forum?authority=*&action=read",
            AtProtoScopes.Space("com.atmoboards.forum", authority: "*", actions: SpaceAction.Read));
    }

    [Fact]
    public void Space_ReadSelf_IsTheNarrowerReadGrant()
    {
        Assert.Equal(
            "space:com.atmoboards.forum?authority=*&action=read_self",
            AtProtoScopes.Space("com.atmoboards.forum", authority: "*", actions: SpaceAction.ReadSelf));
    }

    [Fact]
    public void Space_NamedAuthorityAndKeyWithWriteActions()
    {
        Assert.Equal(
            "space:com.atmoboards.forum?authority=did:plc:abc123&skey=default" +
            "&collection=com.atmoboards.thread&action=create&action=update",
            AtProtoScopes.Space(
                "com.atmoboards.forum",
                authority: "did:plc:abc123",
                skey: "default",
                collections: ["com.atmoboards.thread"],
                actions: SpaceAction.Create | SpaceAction.Update));
    }

    [Fact]
    public void Space_WildcardCollection_WidensBeyondTheDeclaredSet()
    {
        Assert.Equal(
            "space:com.atmoboards.forum?authority=*&collection=*",
            AtProtoScopes.Space("com.atmoboards.forum", authority: "*", collections: ["*"]));
    }

    [Fact]
    public void Space_WildcardCollection_AbsorbsTheRestOfTheList()
    {
        Assert.Equal(
            "space:com.atmoboards.forum?collection=*",
            AtProtoScopes.Space("com.atmoboards.forum", collections: ["com.atmoboards.thread", "*"]));
    }

    [Fact]
    public void Space_MultipleCollections_AreSortedAndDeduplicated()
    {
        Assert.Equal(
            "space:com.atmoboards.forum?collection=com.atmoboards.reply&collection=com.atmoboards.thread",
            AtProtoScopes.Space(
                "com.atmoboards.forum",
                collections: ["com.atmoboards.thread", "com.atmoboards.reply", "com.atmoboards.thread"]));
    }

    [Fact]
    public void Space_ManageVerbs_AreOmittedByDefault()
    {
        Assert.DoesNotContain("manage=", AtProtoScopes.Space("com.atmoboards.forum"), StringComparison.Ordinal);
    }

    [Fact]
    public void Space_AdministrativeGrantWithoutRecordWriteAccess()
    {
        Assert.Equal(
            "space:com.atmoboards.forum?authority=*&action=read_self&manage=update&manage=delete",
            AtProtoScopes.Space(
                "com.atmoboards.forum",
                authority: "*",
                actions: SpaceAction.ReadSelf,
                manage: SpaceManage.Update | SpaceManage.Delete));
    }

    [Fact]
    public void Space_CreateGrant_IsTypicallyUnscopedToAKey()
    {
        // manage=create concerns a space that does not exist yet, so naming a concrete key
        // would be unusual.
        Assert.Equal(
            "space:com.atmoboards.forum?manage=create",
            AtProtoScopes.Space("com.atmoboards.forum", manage: SpaceManage.Create));
    }

    [Fact]
    public void Space_CrossTypeWildcardGrant()
    {
        Assert.Equal(
            "space:*?authority=did:plc:abc123&action=read",
            AtProtoScopes.Space("*", authority: "did:plc:abc123", actions: SpaceAction.Read));
    }

    [Fact]
    public void Space_AuthorityWithAFragment_IsEncoded()
    {
        Assert.Contains("%23", AtProtoScopes.Space("com.example.s", authority: "did:web:example.com#x"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Space_ActionsAreEmittedInTheGrammarsOrder()
    {
        Assert.Equal(
            "space:com.example.s?action=read_self&action=create",
            AtProtoScopes.Space("com.example.s", actions: SpaceAction.Create | SpaceAction.ReadSelf));
    }

    [Fact]
    public void Space_NoActions_IsRejectedRatherThanSilentlyWidened()
    {
        // An omitted action list means SpaceAction.All, so quietly emitting nothing for None
        // would hand back a full read/write grant instead of the zero-record-access one asked
        // for. The grammar cannot express it, so the call fails instead.
        var ex = Assert.Throws<ArgumentException>(() => AtProtoScopes.Space(
            "com.example.foo",
            actions: SpaceAction.None,
            manage: SpaceManage.Update));

        Assert.Equal("actions", ex.ParamName);
    }

    [Fact]
    public void Space_CombinesWithOtherScopes()
    {
        var scope = AtProtoScopes.Combine(
            AtProtoScopes.AtProto,
            AtProtoScopes.Space("com.atmoboards.forum", authority: "*"),
            // Blobs are uploaded through com.atproto.repo.uploadBlob, so a client writing
            // blob-bearing records into a space needs a blob permission alongside its space one.
            AtProtoScopes.Blob("image/*"));

        Assert.Contains("space:com.atmoboards.forum?authority=*", scope, StringComparison.Ordinal);
        Assert.Contains("atproto", scope, StringComparison.Ordinal);
    }

    // ── Space type declarations ──────────────────────────────────

    [Fact]
    public void FromLexicon_ParsesASpaceTypeDeclaration()
    {
        var lexicon = JsonSerializer.Deserialize<JsonElement>("""
        {"lexicon":1,"id":"com.atmoboards.forum","defs":{"main":{
          "type":"space",
          "description":"A discussion forum",
          "key":"any",
          "name":"AtmoBoards Forum",
          "name:lang":{"es":"Foro AtmoBoards"},
          "collections":["com.atmoboards.thread","com.atmoboards.reply"]}}}
        """);

        var declaration = SpaceTypeDeclaration.FromLexicon(lexicon)!;

        Assert.Equal("any", declaration.Key);
        Assert.Equal("AtmoBoards Forum", declaration.Name);
        Assert.Equal("A discussion forum", declaration.Description);
        Assert.Equal(["com.atmoboards.thread", "com.atmoboards.reply"], declaration.Collections);
        Assert.Equal("Foro AtmoBoards", declaration.GetName("es"));
        Assert.Equal("AtmoBoards Forum", declaration.GetName("de"));
        Assert.Equal("AtmoBoards Forum", declaration.GetName(null));
    }

    [Fact]
    public void FromLexicon_NonSpaceLexicon_ReturnsNull()
    {
        var lexicon = JsonSerializer.Deserialize<JsonElement>("""
        {"lexicon":1,"id":"app.bsky.feed.post","defs":{"main":{"type":"record","key":"tid"}}}
        """);

        Assert.Null(SpaceTypeDeclaration.FromLexicon(lexicon));
    }

    [Fact]
    public void FromLexicon_SpaceDefinitionThatIsNotMain_ReturnsNull()
    {
        // A space type declaration must be the `main` definition.
        var lexicon = JsonSerializer.Deserialize<JsonElement>("""
        {"lexicon":1,"id":"com.example.s","defs":{"other":{"type":"space","key":"any","name":"X","collections":[]}}}
        """);

        Assert.Null(SpaceTypeDeclaration.FromLexicon(lexicon));
    }
}
