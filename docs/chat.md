# Chat & Direct Messages

ATProto.NET provides full Bluesky direct messaging support via the `chat.bsky` namespace. Access it through `client.Chat`.

## Prerequisites

Chat requires the `transition:chat.bsky` OAuth scope. All chat requests are automatically proxied via the `atproto-proxy` header — this is handled transparently by the SDK.

## Quick Start

```csharp
var client = new AtProtoClientBuilder()
    .WithInstanceUrl("https://bsky.social")
    .Build();

await client.LoginAsync("alice.bsky.social", "app-password");

// List conversations
var convos = await client.Chat.Convo.ListConvosAsync();
foreach (var convo in convos.Convos)
{
    Console.WriteLine($"Convo with: {convo.Members[0].Handle}");
}
```

## Conversations

### List Conversations

```csharp
var result = await client.Chat.Convo.ListConvosAsync(limit: 25);

foreach (var convo in result.Convos)
{
    Console.WriteLine($"ID: {convo.Id}");
    Console.WriteLine($"Members: {string.Join(", ", convo.Members.Select(m => m.Handle))}");
    Console.WriteLine($"Unread: {convo.UnreadCount}");
    Console.WriteLine($"Last message: {convo.LastMessage}");
}

// Pagination
if (result.Cursor is not null)
{
    var nextPage = await client.Chat.Convo.ListConvosAsync(cursor: result.Cursor);
}
```

### Get or Create a Conversation

```csharp
// Get conversation with a specific user
var convo = await client.Chat.Convo.GetConvoForMembersAsync(
    members: ["did:plc:otherperson"]);

Console.WriteLine($"Convo ID: {convo.Convo.Id}");

// Check conversation availability
var availability = await client.Chat.Convo.GetConvoAvailabilityAsync(
    members: ["did:plc:otherperson"]);
```

### Get a Conversation by ID

```csharp
var convo = await client.Chat.Convo.GetConvoAsync(convoId: "convo-id-here");
```

## Messages

### Send a Message

```csharp
var message = await client.Chat.Convo.SendMessageAsync(
    convoId: "convo-id",
    message: new MessageInput { Text = "Hello from ATProto.NET!" });

Console.WriteLine($"Message ID: {message.Id}");
```

### Send Batch Messages

```csharp
var result = await client.Chat.Convo.SendMessageBatchAsync(items: [
    new() { ConvoId = "convo-1", Message = new MessageInput { Text = "Hello!" } },
    new() { ConvoId = "convo-2", Message = new MessageInput { Text = "Hi there!" } },
]);
```

### Get Messages

```csharp
var messages = await client.Chat.Convo.GetMessagesAsync(
    convoId: "convo-id",
    limit: 50);

foreach (var msg in messages.Messages)
{
    Console.WriteLine($"[{msg.SentAt}] {msg.Sender?.Did}: {msg.Text}");
}
```

### Delete a Message (For Self)

```csharp
await client.Chat.Convo.DeleteMessageForSelfAsync(
    convoId: "convo-id",
    messageId: "message-id");
```

## Conversation Management

### Update Read Status

```csharp
// Mark a specific conversation as read
await client.Chat.Convo.UpdateReadAsync(convoId: "convo-id");

// Mark all conversations as read
await client.Chat.Convo.UpdateAllReadAsync();
```

### Mute and Unmute

```csharp
await client.Chat.Convo.MuteConvoAsync(convoId: "convo-id");
await client.Chat.Convo.UnmuteConvoAsync(convoId: "convo-id");
```

### Accept and Leave

```csharp
// Accept a conversation request
await client.Chat.Convo.AcceptConvoAsync(convoId: "convo-id");

// Leave a conversation
await client.Chat.Convo.LeaveConvoAsync(convoId: "convo-id");
```

### Reactions

```csharp
// Add a reaction
await client.Chat.Convo.AddReactionAsync(
    convoId: "convo-id",
    messageId: "message-id",
    value: "❤️");

// Remove a reaction
await client.Chat.Convo.RemoveReactionAsync(
    convoId: "convo-id",
    messageId: "message-id",
    value: "❤️");
```

## Chat Log

Get the activity log for conversations:

```csharp
var log = await client.Chat.Convo.GetLogAsync();
```

## Account Management

```csharp
// Delete chat account data
await client.Chat.Actor.DeleteAccountAsync();

// Export chat data
var data = await client.Chat.Actor.ExportAccountDataAsync();
```

## Chat Declaration

Control who can message you using a `ChatDeclarationRecord`:

```csharp
// Allow messages from everyone
var record = new ChatDeclarationRecord
{
    AllowIncoming = ChatAllowIncoming.All,
};

// Allow messages only from people you follow
var record = new ChatDeclarationRecord
{
    AllowIncoming = ChatAllowIncoming.Following,
};

// Disable incoming messages
var record = new ChatDeclarationRecord
{
    AllowIncoming = ChatAllowIncoming.None,
};
```

## Proxy Handling

Chat requests require routing through Bluesky's chat service proxy. ATProto.NET handles this automatically:

- Each chat request includes an `atproto-proxy` header targeting `did:web:api.bsky.chat#bsky_chat`
- The proxy header is set per-request, so it doesn't interfere with other XRPC calls
- No manual configuration is needed

## Next Steps

- [API Reference](api-reference.md) — Complete ConvoClient and ChatActorClient methods
- [OAuth Authentication](oauth.md) — Request the `transition:chat.bsky` scope
