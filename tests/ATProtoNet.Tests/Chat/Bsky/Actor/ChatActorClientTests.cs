using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using ATProtoNet.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Tests.Chat.Bsky.Actor;

public class ChatActorClientTests : IDisposable
{
    private readonly HttpStub _stub = new();
    private readonly HttpClient _httpClient;
    private readonly XrpcClient _xrpc;
    private readonly ChatActorClient _actor;

    public ChatActorClientTests()
    {
        _httpClient = new HttpClient(_stub) { BaseAddress = new Uri("https://pds.example.com/") };
        _xrpc = new XrpcClient(_httpClient, _httpClient.BaseAddress!, NullLogger.Instance);
        _xrpc.SetTokens("test-token");
        _actor = new ChatActorClient(_xrpc);
    }

    [Fact]
    public async Task DeleteAccount_PostsWithProxy()
    {
        _stub.On("chat.bsky.actor.deleteAccount", "{}");

        await _actor.DeleteAccountAsync();

        var request = Assert.Single(_stub.To("chat.bsky.actor.deleteAccount"));
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
    }

    [Fact]
    public async Task GetStatusAsync_GetsWithProxy_ReadsTheStatus()
    {
        _stub.On("chat.bsky.actor.getStatus", """{"chatDisabled":false,"canCreateGroups":true,"groupMemberLimit":100}""");

        var status = await _actor.GetStatusAsync();

        var request = Assert.Single(_stub.To("chat.bsky.actor.getStatus"));
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Equal(ServiceProxy.BskyChatHeader, request.Proxy);
        Assert.False(status.ChatDisabled);
        Assert.True(status.CanCreateGroups);
        Assert.Equal(100, status.GroupMemberLimit);
    }

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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
