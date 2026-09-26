using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Serialization;
using static ATProtoNet.Tests.Lexicon.App.Bsky.BskyFixtures;

namespace ATProtoNet.Tests.Lexicon.App.Bsky;

/// <summary>
/// The records added for #127: they read what the network writes, keep fields they do not
/// declare, and write their <c>$type</c>.
/// </summary>
public class BskyRecordTests
{
    [Fact]
    public void VerificationRecord_ReadsANetworkRecord_AndKeepsItsExtraFields()
    {
        // Shaped like the verifications bsky.app issues, which carry an undeclared `issuer`.
        var json = $$"""{"$type":"app.bsky.graph.verification","handle":"bob.test","issuer":"{{AliceDid}}","subject":"{{BobDid}}","createdAt":"2026-09-25T20:17:53.554Z","displayName":"Bob"}""";

        var record = JsonSerializer.Deserialize<VerificationRecord>(json, AtProtoJsonDefaults.Options)!;

        Assert.Equal(BobDid, record.Subject.Value);
        Assert.Equal("bob.test", record.Handle.Value);
        Assert.Equal("Bob", record.DisplayName);
        Assert.Equal("2026-09-25T20:17:53.554Z", record.CreatedAt.ToString());
        Assert.Equal(AliceDid, record.ExtensionData!["issuer"].GetString());

        var written = JsonSerializer.Serialize(record, AtProtoJsonDefaults.Options);
        Assert.StartsWith("""{"$type":"app.bsky.graph.verification",""", written);
        Assert.Contains($"\"issuer\":\"{AliceDid}\"", written, StringComparison.Ordinal);
    }

    [Fact]
    public void ReferenceListOptOutRecord_WritesItsShape()
    {
        var record = new ReferenceListOptOutRecord
        {
            Subject = AtUri.Parse(ListUri),
            CreatedAt = AtDatetime.Parse("2026-09-01T00:00:00.000Z"),
        };

        Assert.Equal(
            $$"""{"$type":"app.bsky.graph.referencelistoptout","subject":"{{ListUri}}","createdAt":"2026-09-01T00:00:00.000Z"}""",
            JsonSerializer.Serialize(record, AtProtoJsonDefaults.Options));
    }

    [Fact]
    public void StatusRecord_ReadsALiveStatusWithItsEmbed()
    {
        // Shaped like the go-live records the Bluesky app writes.
        const string json = """
            {"$type":"app.bsky.actor.status","createdAt":"2026-09-25T20:48:38.302Z","durationMinutes":30,
             "embed":{"$type":"app.bsky.embed.external","external":{"description":"Streaming now","thumb":{"$type":"blob","ref":{"$link":"bafkreifj76geu7wghvlvcbob6piob6hxfo3jvkshxhkyv3ght3n7zb75ey"},"mimeType":"image/jpeg","size":471348},"title":"Live","uri":"https://stream.example.com/alice"}},
             "status":"app.bsky.actor.status#live"}
            """;

        var record = JsonSerializer.Deserialize<StatusRecord>(json, AtProtoJsonDefaults.Options)!;

        Assert.Equal(ActorStatus.Live, record.Status);
        Assert.Equal(30, record.DurationMinutes);
        var embed = Assert.IsType<ExternalEmbed>(record.Embed);
        Assert.Equal("https://stream.example.com/alice", embed.External.Uri);
        Assert.StartsWith(
            """{"$type":"app.bsky.actor.status","status":"app.bsky.actor.status#live","embed":{"$type":"app.bsky.embed.external",""",
            JsonSerializer.Serialize(record, AtProtoJsonDefaults.Options));
    }

    [Fact]
    public void ContentVisibilityDeclarationRecord_RoundTrips()
    {
        const string json = """{"$type":"app.bsky.actor.contentVisibilityDeclaration","hideFromAlgorithmicRecommendations":true}""";

        var record = JsonSerializer.Deserialize<ContentVisibilityDeclarationRecord>(json, AtProtoJsonDefaults.Options)!;

        Assert.True(record.HideFromAlgorithmicRecommendations);
        Assert.Equal(json, JsonSerializer.Serialize(record, AtProtoJsonDefaults.Options));
    }
}
