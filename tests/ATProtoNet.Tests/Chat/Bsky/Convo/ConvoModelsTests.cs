using System.Text.Json;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;

namespace ATProtoNet.Tests.Chat.Bsky.Convo;

public class ConvoModelsTests
{
    [Fact]
    public void ConvoView_Deserializes()
    {
        var json = """
        {
            "id": "convo-1",
            "rev": "rev-1",
            "members": [
                {
                    "did": "did:plc:user1",
                    "handle": "alice.bsky.social",
                    "displayName": "Alice"
                }
            ],
            "muted": false,
            "unreadCount": 3,
            "status": "accepted"
        }
        """;

        var convo = JsonSerializer.Deserialize<ConvoView>(json);

        Assert.NotNull(convo);
        Assert.Equal("convo-1", convo.Id);
        Assert.Equal("rev-1", convo.Rev);
        Assert.Single(convo.Members);
        Assert.Equal("did:plc:user1", convo.Members[0].Did);
        Assert.Equal("alice.bsky.social", convo.Members[0].Handle);
        Assert.Equal("Alice", convo.Members[0].DisplayName);
        Assert.False(convo.Muted);
        Assert.Equal(3, convo.UnreadCount);
        Assert.Equal("accepted", convo.Status);
    }

    [Fact]
    public void MessageView_Deserializes()
    {
        var json = """
        {
            "id": "msg-1",
            "rev": "rev-1",
            "text": "Hello world!",
            "sender": { "did": "did:plc:user1" },
            "sentAt": "2024-06-15T12:00:00Z"
        }
        """;

        var msg = JsonSerializer.Deserialize<MessageView>(json);

        Assert.NotNull(msg);
        Assert.Equal("msg-1", msg.Id);
        Assert.Equal("Hello world!", msg.Text);
        Assert.Equal("did:plc:user1", msg.Sender.Did);
        Assert.Equal("2024-06-15T12:00:00Z", msg.SentAt);
    }

    [Fact]
    public void DeletedMessageView_Deserializes()
    {
        var json = """
        {
            "id": "msg-2",
            "rev": "rev-3",
            "sender": { "did": "did:plc:user2" },
            "sentAt": "2024-06-15T13:00:00Z"
        }
        """;

        var msg = JsonSerializer.Deserialize<DeletedMessageView>(json);

        Assert.NotNull(msg);
        Assert.Equal("msg-2", msg.Id);
        Assert.Equal("did:plc:user2", msg.Sender.Did);
    }

    [Fact]
    public void SendMessageRequest_Serializes()
    {
        var req = new SendMessageRequest
        {
            ConvoId = "convo-1",
            Message = new MessageInput { Text = "Hi there!" },
        };

        var json = JsonSerializer.Serialize(req);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("convo-1", doc.RootElement.GetProperty("convoId").GetString());
        Assert.Equal("Hi there!", doc.RootElement.GetProperty("message").GetProperty("text").GetString());
    }

    [Fact]
    public void ListConvosResponse_Deserializes()
    {
        var json = """
        {
            "cursor": "next-page",
            "convos": [
                {
                    "id": "convo-1",
                    "rev": "rev-1",
                    "members": [],
                    "muted": false,
                    "unreadCount": 0
                }
            ]
        }
        """;

        var resp = JsonSerializer.Deserialize<ListConvosResponse>(json);

        Assert.NotNull(resp);
        Assert.Equal("next-page", resp.Cursor);
        Assert.Single(resp.Convos);
    }

    [Fact]
    public void ChatMemberView_WithOptionalFields()
    {
        var json = """
        {
            "did": "did:plc:user1",
            "handle": "alice.bsky.social",
            "chatDisabled": true
        }
        """;

        var member = JsonSerializer.Deserialize<ChatMemberView>(json);

        Assert.NotNull(member);
        Assert.True(member.ChatDisabled);
        Assert.Null(member.DisplayName);
        Assert.Null(member.Avatar);
    }

    [Fact]
    public void AddReactionRequest_Serializes()
    {
        var req = new AddReactionRequest
        {
            ConvoId = "convo-1",
            MessageId = "msg-1",
            Value = "\u2764\uFE0F",
        };

        var json = JsonSerializer.Serialize(req);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("convo-1", doc.RootElement.GetProperty("convoId").GetString());
        Assert.Equal("msg-1", doc.RootElement.GetProperty("messageId").GetString());
        Assert.Equal("\u2764\uFE0F", doc.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public void BatchMessageItem_Serializes()
    {
        var batch = new SendMessageBatchRequest
        {
            Items =
            [
                new BatchMessageItem
                {
                    ConvoId = "convo-1",
                    Message = new MessageInput { Text = "First" },
                },
                new BatchMessageItem
                {
                    ConvoId = "convo-2",
                    Message = new MessageInput { Text = "Second" },
                },
            ]
        };

        var json = JsonSerializer.Serialize(batch);
        var doc = JsonDocument.Parse(json);
        var items = doc.RootElement.GetProperty("items");

        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("convo-1", items[0].GetProperty("convoId").GetString());
        Assert.Equal("convo-2", items[1].GetProperty("convoId").GetString());
    }
}
