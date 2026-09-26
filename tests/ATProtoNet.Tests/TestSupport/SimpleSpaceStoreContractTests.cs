using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;

namespace ATProtoNet.Tests.TestSupport;

/// <summary>
/// Behaviour every <see cref="ISimpleSpaceStore"/> implementation must have. A derived class
/// supplies <see cref="CreateStore"/>; keep anything implementation-specific (schema,
/// cross-restart persistence) in that derived class instead.
/// </summary>
public abstract class SimpleSpaceStoreContractTests
{
    protected static readonly SpaceUri Space = SpaceUri.Parse("at://did:plc:authority/space/com.example.forum/main");
    protected static readonly Did Owner = Did.Parse("did:plc:authority");

    protected abstract ISimpleSpaceStore CreateStore();

    private static SimpleSpaceRecord Record(SimpleSpaceAppAccess? appAccess = null) =>
        new(Space, Owner, new MemberListPolicy(), new MemberListPolicy(), appAccess ?? new OpenAppAccess());

    [Fact]
    public async Task CreateSpaceAsync_TheSameUriTwice_IsRefused()
    {
        var store = CreateStore();
        var record = Record();

        Assert.True(await store.CreateSpaceAsync(record));
        Assert.False(await store.CreateSpaceAsync(record));
    }

    [Fact]
    public async Task GetSpaceAsync_RoundTripsAllThreePolicyUnions()
    {
        var store = CreateStore();
        await store.CreateSpaceAsync(new SimpleSpaceRecord(
            Space,
            Owner,
            new ManagingAppPolicy { ManagingApp = "did:web:forum.example#forum" },
            new PublicPolicy(),
            new AllowListAppAccess { Allowed = ["https://forum.example/client-metadata.json"] }));

        var loaded = await store.GetSpaceAsync(Space);

        Assert.NotNull(loaded);
        Assert.Equal(Space.Value, loaded.Uri.Value);
        Assert.Equal("did:plc:authority", loaded.Owner);
        var policy = Assert.IsType<ManagingAppPolicy>(loaded.ReadPolicy);
        Assert.Equal("did:web:forum.example#forum", policy.ManagingApp);
        Assert.IsType<PublicPolicy>(loaded.WritePolicy);
        var access = Assert.IsType<AllowListAppAccess>(loaded.AppAccess);
        Assert.Equal(["https://forum.example/client-metadata.json"], access.Allowed);
        Assert.False(loaded.Deleted);
    }

    [Fact]
    public async Task GetSpaceAsync_UnknownSpace_IsNull()
    {
        Assert.Null(await CreateStore().GetSpaceAsync(Space));
    }

    [Fact]
    public async Task UpdateSpaceAsync_ReplacesThePolicy()
    {
        var store = CreateStore();
        var record = Record();
        await store.CreateSpaceAsync(record);

        await store.UpdateSpaceAsync(record with { WritePolicy = new PublicPolicy() });

        var loaded = await store.GetSpaceAsync(Space);
        Assert.IsType<MemberListPolicy>(loaded!.ReadPolicy);
        Assert.IsType<PublicPolicy>(loaded.WritePolicy);
    }

    [Fact]
    public async Task DeleteSpaceAsync_FlagsRatherThanRemoves()
    {
        var store = CreateStore();
        await store.CreateSpaceAsync(Record());

        await store.DeleteSpaceAsync(Space);
        await store.DeleteSpaceAsync(Space);

        var loaded = await store.GetSpaceAsync(Space);
        Assert.NotNull(loaded);
        Assert.True(loaded.Deleted);
    }

    [Fact]
    public async Task Members_ArePutRemovedAndQueried_Idempotently()
    {
        var store = CreateStore();
        await store.CreateSpaceAsync(Record());

        await store.PutMemberAsync(Space, Did.Parse("did:plc:alice"), read: true, write: true);
        await store.PutMemberAsync(Space, Did.Parse("did:plc:alice"), read: true, write: true);
        var alice = await store.GetMemberAsync(Space, Did.Parse("did:plc:alice"));
        Assert.NotNull(alice);
        Assert.True(alice.Read);
        Assert.True(alice.Write);
        Assert.Null(await store.GetMemberAsync(Space, Did.Parse("did:plc:bob")));

        await store.RemoveMemberAsync(Space, Did.Parse("did:plc:alice"));
        await store.RemoveMemberAsync(Space, Did.Parse("did:plc:alice"));
        Assert.Null(await store.GetMemberAsync(Space, Did.Parse("did:plc:alice")));
    }

    [Fact]
    public async Task PutMemberAsync_AnExistingMember_ReplacesBothFlags()
    {
        var store = CreateStore();
        await store.CreateSpaceAsync(Record());

        await store.PutMemberAsync(Space, Did.Parse("did:plc:alice"), read: true, write: false);
        await store.PutMemberAsync(Space, Did.Parse("did:plc:alice"), read: false, write: true);

        var member = Assert.Single((await store.ListMembersAsync(Space, 10, null)).Members);
        Assert.Equal("did:plc:alice", member.Did);
        Assert.False(member.Read);
        Assert.True(member.Write);
    }

    [Fact]
    public async Task PutMemberAsync_FalseFlags_AreStoredRatherThanDefaulted()
    {
        var store = CreateStore();
        await store.CreateSpaceAsync(Record());

        await store.PutMemberAsync(Space, Did.Parse("did:plc:alice"), read: false, write: false);

        var member = await store.GetMemberAsync(Space, Did.Parse("did:plc:alice"));
        Assert.NotNull(member);
        Assert.False(member.Read);
        Assert.False(member.Write);
    }

    [Fact]
    public async Task PutMemberAsync_ForASpaceThatDoesNotExist_IsANoOp()
    {
        var store = CreateStore();

        await store.PutMemberAsync(Space, Did.Parse("did:plc:alice"), read: true, write: true);

        Assert.Null(await store.GetMemberAsync(Space, Did.Parse("did:plc:alice")));
    }

    [Fact]
    public async Task ListMembersAsync_PagesByDid_AndTheCursorResumesWhereItLeftOff()
    {
        var store = CreateStore();
        await store.CreateSpaceAsync(Record());
        foreach (var did in new[] { Did.Parse("did:plc:c"), Did.Parse("did:plc:a"), Did.Parse("did:plc:b") })
            await store.PutMemberAsync(Space, did, read: true, write: did == "did:plc:b");

        var first = await store.ListMembersAsync(Space, 2, null);
        Assert.Equal(["did:plc:a", "did:plc:b"], first.Members.Select(m => m.Did.Value));
        Assert.Equal([false, true], first.Members.Select(m => m.Write));
        Assert.Equal("did:plc:b", first.Cursor);

        var second = await store.ListMembersAsync(Space, 2, first.Cursor);
        Assert.Equal(["did:plc:c"], second.Members.Select(m => m.Did.Value));
        Assert.Null(second.Cursor);
    }
}
