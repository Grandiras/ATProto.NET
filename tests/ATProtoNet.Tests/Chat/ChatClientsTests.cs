namespace ATProtoNet.Tests.Chat;

public class ChatClientsTests
{
    [Fact]
    public void Chat_EverySubClient_IsRegistered()
    {
        using var client = new AtProtoClient();

        Assert.NotNull(client.Chat.Convo);
        Assert.NotNull(client.Chat.Actor);
        Assert.NotNull(client.Chat.Group);
        Assert.NotNull(client.Chat.Notification);
        Assert.NotNull(client.Chat.Moderation);
    }
}
