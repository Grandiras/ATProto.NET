using System.Text.Json;
using ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;
using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Spaces;

/// <summary>
/// The Space and SimpleSpace models follow the SDK's union and extension-data pattern: the
/// <c>simplespace</c> policy unions are open, <c>applyWrites</c> is closed as its Lexicon says,
/// and object models keep fields they do not declare.
/// </summary>
public class SpaceUnionTests
{
    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    [Fact]
    public void GetSpace_WithAPolicyVariantFromANewerSchema_ReadsItAsUnknownAndWritesItBack()
    {
        // e.g. the "unauthenticated" read policy proposed upstream (bluesky-social/atproto#5462).
        const string json =
            """{"uri":"at://did:plc:bbbbbbbbbbbbbbbbbbbbbbbb/space/com.atmoboards.forum/default","readPolicy":{"$type":"com.atproto.simplespace.defs#unauthenticatedPolicy","cache":true},"writePolicy":{"$type":"com.atproto.simplespace.defs#memberListPolicy"},"appAccess":{"$type":"com.atproto.simplespace.defs#futureAccess","n":1}}""";

        var response = JsonSerializer.Deserialize<GetSimpleSpaceResponse>(json, Options)!;

        var read = Assert.IsType<UnknownSimpleSpaceUserPolicy>(response.ReadPolicy);
        Assert.Equal("com.atproto.simplespace.defs#unauthenticatedPolicy", read.Type);
        Assert.IsType<MemberListPolicy>(response.WritePolicy);
        Assert.Equal("com.atproto.simplespace.defs#futureAccess", Assert.IsType<UnknownSimpleSpaceAppAccess>(response.AppAccess).Type);

        Assert.Equal(
            """{"$type":"com.atproto.simplespace.defs#unauthenticatedPolicy","cache":true}""",
            JsonSerializer.Serialize<SimpleSpaceUserPolicy>(response.ReadPolicy, Options));
    }

    [Fact]
    public void KnownPolicyVariant_KeepsFieldsItDoesNotDeclare()
    {
        const string json =
            """{"$type":"com.atproto.simplespace.defs#managingAppPolicy","managingApp":"did:web:app.example.com#forum","since":"2026"}""";

        var policy = Assert.IsType<ManagingAppPolicy>(JsonSerializer.Deserialize<SimpleSpaceUserPolicy>(json, Options));

        Assert.Equal("did:web:app.example.com#forum", policy.ManagingApp);
        Assert.Equal("2026", policy.ExtensionData!["since"].GetString());
        Assert.Contains("\"since\":\"2026\"", JsonSerializer.Serialize<SimpleSpaceUserPolicy>(policy, Options), StringComparison.Ordinal);
    }

    [Fact]
    public void KnownPolicyVariant_WritesItsDiscriminatorFirst()
    {
        Assert.Equal(
            """{"$type":"com.atproto.simplespace.defs#publicPolicy"}""",
            JsonSerializer.Serialize<SimpleSpaceUserPolicy>(new PublicPolicy(), Options));
        Assert.Equal(
            """{"$type":"com.atproto.simplespace.defs#allowList","allowed":["https://app.example.com/client-metadata.json"]}""",
            JsonSerializer.Serialize<SimpleSpaceAppAccess>(
                new AllowListAppAccess { Allowed = ["https://app.example.com/client-metadata.json"] }, Options));
    }

    [Fact]
    public void ApplyWritesOperation_WithAnUnknownType_IsRefused()
    {
        // The Lexicon marks `writes` closed.
        const string json = """{"$type":"com.atproto.space.applyWrites#move","collection":"com.example.n","rkey":"a"}""";

        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<SpaceWriteOp>(json, Options));
    }

    [Fact]
    public void ApplyWritesOperation_RoundTripsItsDiscriminator()
    {
        var op = new SpaceDeleteOp { Collection = ATProtoNet.Identity.Nsid.Parse("com.example.n"), Rkey = ATProtoNet.Identity.RecordKey.Parse("a") };

        var json = JsonSerializer.Serialize<SpaceWriteOp>(op, Options);

        Assert.StartsWith("""{"$type":"com.atproto.space.applyWrites#delete",""", json, StringComparison.Ordinal);
        Assert.IsType<SpaceDeleteOp>(JsonSerializer.Deserialize<SpaceWriteOp>(json, Options));
    }

    [Fact]
    public void ClosedSpaceUnion_TakesNoRegisteredVariants()
    {
        var ex = Assert.Throws<ArgumentException>(
            () => LexiconTypeRegistry.Instance.RegisterUnionVariant<SpaceWriteOp, RegisteredMove>("com.example.move"));

        Assert.Contains("closed", ex.Message, StringComparison.Ordinal);
        Assert.Empty(LexiconTypeRegistry.Instance.GetUnionVariants(typeof(SpaceWriteOp)));
    }

    [Fact]
    public void ListRepos_EntriesKeepFieldsTheyDoNotDeclare()
    {
        const string json =
            """{"repos":[{"did":"did:plc:aaaaaaaaaaaaaaaaaaaaaaaa","rev":"3l6oveex3ii2l","hash":{"$bytes":"AAEC"},"status":"active"}]}""";

        var response = JsonSerializer.Deserialize<ListSpaceReposResponse>(json, Options)!;

        Assert.Equal("active", Assert.Single(response.Repos).ExtensionData!["status"].GetString());
    }

    [Fact]
    public void SimpleSpaceMember_KeepsFieldsItDoesNotDeclare()
    {
        const string json = """{"did":"did:plc:aaaaaaaaaaaaaaaaaaaaaaaa","read":true,"write":false,"role":"moderator"}""";

        var member = JsonSerializer.Deserialize<SimpleSpaceMember>(json, Options)!;

        Assert.Equal("moderator", member.ExtensionData!["role"].GetString());
    }

    private sealed class RegisteredMove : SpaceWriteOp;
}
