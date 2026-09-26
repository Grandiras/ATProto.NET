# Chat & Direct Messages

ATProto.NET provides full Bluesky chat support, direct and group, via the `chat.bsky` namespace. Access it through `client.Chat`:

| Sub-client | Lexicons | Namespace |
|---|---|---|
| `client.Chat.Convo` | `chat.bsky.convo.*`: conversations, messages, reactions, the log | `ATProtoNet.Lexicon.Chat.Bsky.Convo` |
| `client.Chat.Group` | `chat.bsky.group.*`: groups, members, join links, join requests | `ATProtoNet.Lexicon.Chat.Bsky.Group` |
| `client.Chat.Actor` | `chat.bsky.actor.*`: chat status, account data | `ATProtoNet.Lexicon.Chat.Bsky.Actor` |
| `client.Chat.Notification` | `chat.bsky.notification.*`: notification preferences | `ATProtoNet.Lexicon.Chat.Bsky.Notification` |
| `client.Chat.Moderation` | `chat.bsky.moderation.*`: for moderation services | `ATProtoNet.Lexicon.Chat.Bsky.Moderation` |

Message embeds (`MessageRecordEmbed`, `JoinLinkEmbed` and their views) are in `ATProtoNet.Lexicon.Chat.Bsky.Embed`.

## Prerequisites

Chat requires the `transition:chat.bsky` OAuth scope. All chat requests are automatically proxied via the `atproto-proxy` header — this is handled transparently by the SDK.

## Quick Start

```csharp
var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = "https://bsky.social" });

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
    var title = convo.Kind switch
    {
        GroupConvo group => $"{group.Name} ({group.MemberCount} members)",
        _ => string.Join(", ", convo.Members.Select(m => m.Handle)),
    };
    var preview = convo.LastMessage switch
    {
        MessageView m => m.Text,
        DeletedMessageView => "(deleted)",
        SystemMessageView => "(group update)",
        _ => null,
    };
    Console.WriteLine($"{title}: {preview} — {convo.UnreadCount} unread");
}

// Pagination
if (result.Cursor is not null)
{
    var nextPage = await client.Chat.Convo.ListConvosAsync(cursor: result.Cursor);
}

// Or let the SDK fetch the pages; filter by status, read state, kind and lock status
await foreach (var convo in client.Chat.Convo.EnumerateConvosAsync(
    status: ConvoStatus.Accepted, kind: ConvoKinds.Group, lockStatus: ConvoLockStatus.Unlocked))
{
    Console.WriteLine(convo.Id);
}
```

A conversation's `Kind` is a `DirectConvo` or a `GroupConvo`. Like every union in the SDK it is
open: a kind a newer chat service adds reads as `UnknownConvoKind` instead of failing the page (see
[Unions and unknown fields](custom-records.md#unions-and-unknown-fields)).

### Requests and Unread Counts

```csharp
// Incoming conversation requests, and the group join requests you made
await foreach (var request in client.Chat.Convo.EnumerateConvoRequestsAsync())
{
    switch (request)
    {
        case ConvoView convo:
            Console.WriteLine($"Request from {convo.Members[0].Handle}");
            break;
        case JoinRequestConvoView join:
            Console.WriteLine($"Waiting to join {join.Name} since {join.Viewer.RequestedAt}");
            break;
    }
}

var counts = await client.Chat.Convo.GetUnreadCountsAsync();
Console.WriteLine($"{counts.UnreadAcceptedConvos} unread, {counts.UnreadRequestConvos} requests");
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

### Members

`ConvoView.Members` lists both members of a direct conversation, but only the notable members of a
group. List all of them with `GetConvoMembersAsync` / `EnumerateConvoMembersAsync`. A member's
`Kind` says how it belongs to the conversation:

```csharp
await foreach (var member in client.Chat.Convo.EnumerateConvoMembersAsync("convo-id"))
{
    var role = member.Kind switch
    {
        GroupConvoMember { Role: ChatMemberRole.Owner } => "owner",
        GroupConvoMember m => $"member, added by {m.AddedBy?.Handle}",
        PastGroupConvoMember => "former member",
        _ => "member",
    };
    Console.WriteLine($"{member.Handle}: {role}");
}
```

## Messages

### Send a Message

```csharp
var message = await client.Chat.Convo.SendMessageAsync(
    convoId: "convo-id",
    message: new MessageInput { Text = "Hello from ATProto.NET!" });

Console.WriteLine($"Message ID: {message.Id}");
```

Messages take rich-text facets, an embed and a reply reference, like posts:

```csharp
var (text, facets) = new RichTextBuilder()
    .Text("Have a look, ")
    .Mention(Handle.Parse("bob.bsky.social"), Did.Parse("did:plc:bob"))
    .Build();

await client.Chat.Convo.SendMessageAsync("convo-id", new MessageInput
{
    Text = text,
    Facets = facets,
    // Quote a post: a MessageRecordEmbed. Share a group: a JoinLinkEmbed { Code = … }.
    Embed = new MessageRecordEmbed { Record = new StrongRef { Uri = postUri, Cid = postCid } },
    // Reply to a message in the same conversation
    ReplyTo = new MessageReplyRef { MessageId = "message-id" },
});
```

### Send Batch Messages

```csharp
var result = await client.Chat.Convo.SendMessageBatchAsync(items: [
    new() { ConvoId = "convo-1", Message = new MessageInput { Text = "Hello!" } },
    new() { ConvoId = "convo-2", Message = new MessageInput { Text = "Hi there!" } },
]);
```

### Get Messages

Messages come typed: a `MessageView`, a `DeletedMessageView`, or in groups a `SystemMessageView`
that reports what happened to the group. `RelatedProfiles` has the profile of everyone the page
refers to:

```csharp
var page = await client.Chat.Convo.GetMessagesAsync(convoId: "convo-id", limit: 50);
var profiles = page.RelatedProfiles?.ToDictionary(p => p.Did) ?? [];

foreach (var msg in page.Messages)
{
    switch (msg)
    {
        case MessageView m:
            var author = profiles.GetValueOrDefault(m.Sender.Did)?.Handle.ToString() ?? m.Sender.Did.ToString();
            Console.WriteLine($"[{m.SentAt}] {author}: {m.Text}");
            if (m.ReplyTo is MessageView parent)
                Console.WriteLine($"  ↳ in reply to: {parent.Text}");
            foreach (var reaction in m.Reactions ?? [])
                Console.WriteLine($"  {reaction.Value} from {reaction.Sender.Did}");
            if (m.Embed is JoinLinkEmbedView { JoinLinkPreview: JoinLinkPreviewView group })
                Console.WriteLine($"  invite to {group.Name}");
            break;
        case SystemMessageView { Data: SystemMessageDataAddMember added }:
            Console.WriteLine($"{added.AddedBy.Did} added {added.Member.Did}");
            break;
        case DeletedMessageView:
            Console.WriteLine("(deleted)");
            break;
    }
}

// Every message in the conversation, fetching pages as needed
await foreach (var msg in client.Chat.Convo.EnumerateMessagesAsync("convo-id"))
{
    // …
}
```

A reply to a message sent before you joined a group carries a `MessageBeforeUserJoinedGroupView`
as its `ReplyTo`: it tells you there is a parent without showing it.

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
await client.Chat.Convo.UpdateAllReadAsync(status: ConvoStatus.Request);
```

`UpdateReadAsync`, `MuteConvoAsync`, `UnmuteConvoAsync`, `LockConvoAsync` and `UnlockConvoAsync`
return the conversation after the change; `AddReactionAsync` and `RemoveReactionAsync` return the
message.

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

## Group Chats

A group is a conversation whose `Kind` is a `GroupConvo`. Messages, reactions, muting, reading and
leaving work as in a direct conversation, through `client.Chat.Convo`; creating the group and
managing its members, join link and join requests goes through `client.Chat.Group`. Methods that
change the group are for its owner; anyone else gets an `InsufficientRole` error.

### Creating a Group

New accounts may not create groups, and a group has a member limit. Check first:

```csharp
var status = await client.Chat.Actor.GetStatusAsync();
if (!status.CanCreateGroups)
    return;

var group = await client.Chat.Group.CreateGroupAsync(
    name: "Book club",
    members: [Did.Parse("did:plc:alice"), Did.Parse("did:plc:bob")]);
```

Each member is added with a request they accept like any conversation request, and only accounts
whose `ChatDeclarationRecord.AllowGroupInvites` allows it can be added. `CreateGroupAsync` is not
idempotent: every call creates a new group.

```csharp
// Owner only
await client.Chat.Group.AddMembersAsync(group.Id, [Did.Parse("did:plc:carol")]);
await client.Chat.Group.RemoveMembersAsync(group.Id, [Did.Parse("did:plc:bob")]);
await client.Chat.Group.EditGroupAsync(group.Id, name: "Book club 📚");

// Lock the group: no new messages or reactions until it is unlocked
await client.Chat.Convo.LockConvoAsync(group.Id);
await client.Chat.Convo.UnlockConvoAsync(group.Id);

// The groups you share with someone
await foreach (var shared in client.Chat.Group.EnumerateMutualGroupsAsync(Did.Parse("did:plc:alice")))
    Console.WriteLine(((GroupConvo)shared.Kind!).Name);
```

### Join Links

A group has at most one join link. Its `JoinRule` says who may use it (`JoinRule.Anyone` or
`JoinRule.FollowedByOwner`), and with `requireApproval` the owner approves each request:

```csharp
var link = await client.Chat.Group.CreateJoinLinkAsync(
    group.Id, JoinRule.FollowedByOwner, requireApproval: true);

// Change a setting; the ones you leave null stay as they are
await client.Chat.Group.EditJoinLinkAsync(group.Id, requireApproval: false);

// Switch it off and on again; the code stays the same
await client.Chat.Group.DisableJoinLinkAsync(group.Id);
await client.Chat.Group.EnableJoinLinkAsync(group.Id);

// Share it in a message
await client.Chat.Convo.SendMessageAsync(otherConvoId, new MessageInput
{
    Text = "Join our book club!",
    Embed = new JoinLinkEmbed { Code = link.Code },
});
```

Anyone, even signed out, can preview the groups behind link codes. There is one preview per code,
in order:

```csharp
var previews = await client.Chat.Group.GetJoinLinkPreviewsAsync(["abc123", "def456"]);
foreach (var preview in previews.JoinLinkPreviews)
{
    Console.WriteLine(preview switch
    {
        JoinLinkPreviewView p => $"{p.Name}: {p.MemberCount}/{p.MemberLimit}, owned by {p.Owner.Handle}",
        DisabledJoinLinkPreviewView d => $"{d.Code}: disabled",
        InvalidJoinLinkPreviewView i => $"{i.Code}: no such link",
        _ => "unknown",
    });
}
```

### Join Requests

```csharp
// Joining: you are in at once, or the owner has to approve
var result = await client.Chat.Group.RequestJoinAsync("abc123");
if (result.Status == RequestJoinStatus.Joined)
    Console.WriteLine($"Joined {result.Convo!.Id}");

// Changed your mind while it is pending
await client.Chat.Group.WithdrawJoinRequestAsync(convoId);

// The owner's side
await foreach (var request in client.Chat.Group.EnumerateJoinRequestsAsync(group.Id))
{
    if (request.RequestedBy.Handle.ToString().EndsWith(".example.com"))
        await client.Chat.Group.ApproveJoinRequestAsync(group.Id, request.RequestedBy.Did);
    else
        await client.Chat.Group.RejectJoinRequestAsync(group.Id, request.RequestedBy.Did);
}
await client.Chat.Group.UpdateJoinRequestsReadAsync(group.Id);
```

The owner sees the pending count as `GroupConvo.JoinRequestCount` (capped at 21) and the unread
count as `UnreadJoinRequestCount`.

### Moderation Hooks

- **Moderation locks.** A group can be locked by moderation rather than by its owner, for example
  when the owner's account is taken down. Then `GroupConvo.LockStatusModerationOverride` is `true`,
  and `UnlockConvoAsync` fails with `ConvoLockedByModeration`.
- **Moderation services** reach conversations they are not a member of through
  `client.Chat.Moderation` (`chat.bsky.moderation.*`): `GetConvoAsync` / `GetConvosAsync` (a
  `ModerationConvoView`, without viewer data), `EnumerateConvoMembersAsync`,
  `GetMessageContextAsync` (a reported message with the messages around it),
  `GetActorMetadataAsync` (an account's activity) and `UpdateActorAccessAsync` (revoke or restore
  chat access). The chat service answers these only for moderation services, so unlike the rest of
  `client.Chat` they carry no fixed proxy header: they follow the client-wide default, as the Ozone
  clients do.

```csharp
// A moderator working through Ozone
client.SetProxy("did:plc:ozone-service#atproto_labeler");

var context = await client.Chat.Moderation.GetMessageContextAsync(
    messageId: "reported-message-id", convoId: "convo-id", before: 10, after: 5);
await client.Chat.Moderation.UpdateActorAccessAsync(
    Did.Parse("did:plc:spammer"), allowAccess: false, reference: "ozone-event-123");
```

The moderation event stream, `chat.bsky.moderation.subscribeModEvents`, is a WebSocket
subscription on the chat service itself, so it is not on `client.Chat`: read it with
`ChatModerationEventConsumer` (`ATProtoNet.Streaming`). The endpoint is private, and every
connection, reconnects included, asks `GetAccessTokenAsync` for a fresh bearer token: a
service-auth token whose audience is the chat service and whose `lxm` is the method. Each event is
a `ChatModerationEvent` (`ConvoFirstMessageEvent`, `GroupChatCreatedEvent`,
`GroupChatMemberAddedEvent`, `GroupChatMemberLeftEvent`, `GroupChatUpdatedEvent`,
`ChatAcceptedEvent`, `RateLimitExceededEvent`, …), and one this SDK does not model reads as
`UnknownChatModerationEvent`.

```csharp
var consumer = new ChatModerationEventConsumer(new ChatModerationEventConsumerOptions
{
    ServiceUrl = "wss://api.bsky.chat",
    GetAccessTokenAsync = ct => MintServiceAuthAsync(
        audience: "did:web:api.bsky.chat", lxm: "chat.bsky.moderation.subscribeModEvents", ct),
});

// ChatModerationEventConsumer.BeginningCursor replays from the start; null starts live.
await foreach (var evt in consumer.ConsumeAsync(cursor: savedRev, stoppingToken))
{
    switch (evt)
    {
        case GroupChatMemberAddedEvent added:
            await ReviewAsync(added.ConvoId, added.SubjectDid);
            break;
        case RateLimitExceededEvent limited:
            metrics.RateLimited(limited.ActorDid, limited.Endpoint);
            break;
    }

    savedRev = evt.Rev;   // the cursor: persist it yourself
}
```

The consumer reconnects after the last `Rev` it delivered, and follows the same
`StreamReconnectPolicy` and error handling as the firehose consumers (see
[Firehose Streaming](firehose.md#reconnecting-and-errors)). The cursor is a revision string, not a
sequence number, so it is not kept in an `IStreamCursorStore`.

## Chat Log

The log reports every change to your conversations as a typed entry. Each has a `ConvoId` and a
`Rev`; switch on the entry to handle the ones you care about:

```csharp
await foreach (var entry in client.Chat.Convo.EnumerateLogAsync())
{
    switch (entry)
    {
        case LogCreateMessage { Message: MessageView m }:
            Console.WriteLine($"New message in {entry.ConvoId}: {m.Text}");
            break;
        case LogAddReaction added:
            Console.WriteLine($"{added.Reaction.Value} on {entry.ConvoId}");
            break;
        case LogMemberJoin join:
            Console.WriteLine($"Someone joined {entry.ConvoId}");
            break;
        case LogIncomingJoinRequest request:
            Console.WriteLine($"{request.Member.Handle} wants to join {entry.ConvoId}");
            break;
        case UnknownConvoLogEntry unknown:
            // An event type newer than this SDK; unknown.Raw holds it
            break;
    }
}
```

## Account Management

```csharp
// Chat status: disabled account, group creation, group size
var status = await client.Chat.Actor.GetStatusAsync();

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

### Notification Preferences

`chat.bsky.notification` replaces the deprecated `chat` entry of the Bluesky notification
preferences:

```csharp
var prefs = await client.Chat.Notification.GetPreferencesAsync();
Console.WriteLine($"Chats: {prefs.Chat.Include}, push {prefs.Chat.Push}");

// Only the preferences you pass change
await client.Chat.Notification.PutPreferencesAsync(
    chatRequest: new ChatPreference { Include = ChatPreferenceInclude.Follows, Push = false });
```

## Chat Declaration

Control who can message you, and who can add you to groups, using a `ChatDeclarationRecord`:

```csharp
// Allow messages from everyone
var record = new ChatDeclarationRecord
{
    AllowIncoming = ChatAllowIncoming.All,
};

// Allow messages only from people you follow, and no group invitations
var record = new ChatDeclarationRecord
{
    AllowIncoming = ChatAllowIncoming.Following,
    AllowGroupInvites = ChatAllowIncoming.None,
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

The exception is `client.Chat.Moderation`, which follows the client-wide proxy (see
[Moderation Hooks](#moderation-hooks)).

## Next Steps

- [API Reference](api-reference.md) — The chat sub-clients
- [OAuth Authentication](oauth.md) — Request the `transition:chat.bsky` scope
