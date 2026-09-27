using System.Text.Json;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;

namespace ATProtoNet.Tests.Chat.Bsky.Actor;

/// <summary>
/// <c>DeleteAccountAsync</c> and <c>GetStatusAsync</c> are covered by the request/response table
/// in <see cref="ATProtoNet.Tests.Lexicon.EndpointRequestTests"/>.
/// </summary>
public class ChatActorClientTests
{
    [Fact]
    public void ChatDeclarationRecord_WithGroupInvites_WritesTheLexiconShape()
    {
        var record = new ChatDeclarationRecord
        {
            AllowIncoming = ChatAllowIncoming.Following,
            AllowGroupInvites = ChatAllowIncoming.None,
        };

        Assert.Equal(
            """{"$type":"chat.bsky.actor.declaration","allowIncoming":"following","allowGroupInvites":"none"}""",
            JsonSerializer.Serialize(record, ATProtoNet.Serialization.AtProtoJsonDefaults.Options));
    }
}
