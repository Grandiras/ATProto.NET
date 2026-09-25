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

// Or let the SDK fetch the pages
await foreach (var convo in client.Chat.Convo.EnumerateConvosAsync(status: "accepted"))
{
    Console.WriteLine(convo.Id);
}
```

### Get or Create a Conversation

Members are DIDs (`ATProtoNet.Identity.Did`):

```csharp
// Get conversation with a specific user
var convo = await client.Chat.Convo.GetConvoForMembersAsync(
    members: [Did.Parse("did:plc:otherperson")]);

Console.WriteLine($"Convo ID: {convo.Convo.Id}");

// Check conversation availability
var availability = await client.Chat.Convo.GetConvoAvailabilityAsync(
    members: [Did.Parse("did:plc:otherperson")]);
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

Each entry is the raw JSON of a `chat.bsky.convo.defs#messageView` or `#deletedMessageView`;
check `$type` before reading it:

```csharp
var messages = await client.Chat.Convo.GetMessagesAsync(
    convoId: "convo-id",
    limit: 50);

foreach (var msg in messages.Messages)
{
    if (msg.GetProperty("$type").GetString() != "chat.bsky.convo.defs#messageView")
        continue;

    var view = msg.Deserialize<MessageView>(AtProtoJsonDefaults.Options)!;
    Console.WriteLine($"[{view.SentAt}] {view.Sender.Did}: {view.Text}");
}

// Every message in the conversation, fetching pages as needed
await foreach (var msg in client.Chat.Convo.EnumerateMessagesAsync("convo-id"))
{
    Console.WriteLine(msg.GetProperty("id").GetString());
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

// Mark all conversations as read, or only the accepted ones or the requests
var result = await client.Chat.Convo.UpdateAllReadAsync();
Console.WriteLine($"{result.UpdatedCount} conversations marked read");
await client.Chat.Convo.UpdateAllReadAsync(status: "request");
```

`UpdateReadAsync`, `MuteConvoAsync` and `UnmuteConvoAsync` return the conversation after the
change; `AddReactionAsync` and `RemoveReactionAsync` return the message.

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

// Or walk it until the chat service has no newer entries
await foreach (var entry in client.Chat.Convo.EnumerateLogAsync())
{
    Console.WriteLine($"{entry.Type} in {entry.ConvoId}");
}
```

## Account Management

```csharp
// Delete chat account data
await client.Chat.Actor.DeleteAccountAsync();

// Export chat data: JSON Lines, one object per line
await using var export = await client.Chat.Actor.ExportAccountDataAsync();
using var reader = new StreamReader(export.Content);
while (await reader.ReadLineAsync() is { Length: > 0 } line)
{
    using var item = JsonDocument.Parse(line);
    // …
}
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
