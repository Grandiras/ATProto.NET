# Ozone Moderation

ATProto.NET provides full support for the `tools.ozone.*` namespace — the content moderation toolkit used by Bluesky. Access it through `client.Ozone`.

## Sub-Clients

| Client | Namespace | Description |
|--------|-----------|-------------|
| `Ozone.Moderation` | `tools.ozone.moderation` | Moderation events, reports, review |
| `Ozone.Communication` | `tools.ozone.communication` | Email templates |
| `Ozone.Team` | `tools.ozone.team` | Team member management |
| `Ozone.Set` | `tools.ozone.set` | Named sets of values (DIDs, URIs) |
| `Ozone.Server` | `tools.ozone.server` | Server configuration |
| `Ozone.Signature` | `tools.ozone.signature` | Signature correlation & analysis |

## Moderation

### Emit Moderation Events

```csharp
// Take down content
await client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
{
    Event = new ModerationEventTakedown
    {
        Comment = "Violates community guidelines",
    },
    Subject = new RepoSubject { Did = "did:plc:abc123" },
    CreatedBy = client.Did!,
});

// Apply a label
await client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
{
    Event = new ModerationEventLabel
    {
        CreateLabelVals = ["spam"],
        NegateLabelVals = [],
        Comment = "Spam account",
    },
    Subject = new RepoSubject { Did = "did:plc:abc123" },
    CreatedBy = client.Did!,
});

// Add a comment
await client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
{
    Event = new ModerationEventComment
    {
        Comment = "Reviewing this account",
    },
    Subject = new RepoSubject { Did = "did:plc:abc123" },
    CreatedBy = client.Did!,
});
```

### Query Events

```csharp
var events = await client.Ozone.Moderation.QueryEventsAsync(
    subject: "did:plc:abc123",
    limit: 50);

foreach (var evt in events.Events)
{
    Console.WriteLine($"Event: {evt.Event} at {evt.CreatedAt}");
}
```

### Query Subjects Under Review

```csharp
var subjects = await client.Ozone.Moderation.QuerySubjectsAsync(limit: 25);

foreach (var subject in subjects.Subjects)
{
    Console.WriteLine($"Subject: {subject.Subject}");
    Console.WriteLine($"Review state: {subject.SubjectReviewState}");
}
```

### Get Moderation Record/Repo Info

```csharp
var record = await client.Ozone.Moderation.GetRecordAsync(uri: "at://did:plc:abc/app.bsky.feed.post/123");
var repo = await client.Ozone.Moderation.GetRepoAsync(did: "did:plc:abc123");
```

### Search Repositories

```csharp
var results = await client.Ozone.Moderation.SearchReposAsync(query: "spam");
```

## Communication Templates

Manage email templates for moderator communications:

```csharp
// Create a template
var template = await client.Ozone.Communication.CreateTemplateAsync(new CreateTemplateRequest
{
    Name = "first-warning",
    Subject = "Community Guidelines Warning",
    ContentMarkdown = "Your account has been flagged for violating our community guidelines...",
});

// List all templates
var templates = await client.Ozone.Communication.ListTemplatesAsync();

// Update a template
await client.Ozone.Communication.UpdateTemplateAsync(new UpdateTemplateRequest
{
    Id = template.Id,
    ContentMarkdown = "Updated warning text...",
});

// Delete a template
await client.Ozone.Communication.DeleteTemplateAsync(templateId: template.Id);
```

## Team Management

```csharp
// Add a team member
await client.Ozone.Team.AddMemberAsync(new AddMemberRequest
{
    Did = "did:plc:newmoderator",
    Role = TeamMemberRole.Moderator,
});

// List team members
var members = await client.Ozone.Team.ListMembersAsync();

// Update a member's role
await client.Ozone.Team.UpdateMemberAsync(new UpdateMemberRequest
{
    Did = "did:plc:newmoderator",
    Role = TeamMemberRole.Admin,
});

// Remove a team member
await client.Ozone.Team.DeleteMemberAsync(did: "did:plc:newmoderator");
```

## Named Sets

Manage named collections of values for moderation rules:

```csharp
// Create or update a set
await client.Ozone.Set.UpsertSetAsync(new UpsertSetRequest
{
    Name = "blocked-domains",
    Description = "Domains blocked for spam",
});

// Add values to a set
await client.Ozone.Set.AddValuesAsync(new AddValuesRequest
{
    Name = "blocked-domains",
    Values = ["spam-site.example.com", "bad-domain.example.com"],
});

// Get values from a set
var values = await client.Ozone.Set.GetValuesAsync(name: "blocked-domains");

// Query all sets
var sets = await client.Ozone.Set.QuerySetsAsync();

// Remove values
await client.Ozone.Set.DeleteValuesAsync(new DeleteValuesRequest
{
    Name = "blocked-domains",
    Values = ["spam-site.example.com"],
});

// Delete a set
await client.Ozone.Set.DeleteSetAsync(name: "blocked-domains");
```

## Signature Analysis

Find related accounts through signature correlation:

```csharp
// Find correlated signatures
var correlation = await client.Ozone.Signature.FindCorrelationAsync(dids: ["did:plc:abc", "did:plc:def"]);

// Search accounts by signature
var accounts = await client.Ozone.Signature.SearchAccountsAsync(values: ["some-signal"]);

// Find related accounts
var related = await client.Ozone.Signature.FindRelatedAccountsAsync(did: "did:plc:abc123");
```

## Server Configuration

```csharp
var config = await client.Ozone.Server.GetConfigAsync();
```

## Moderation Event Types

| Event Type | Description |
|------------|-------------|
| `ModerationEventTakedown` | Take down content or account |
| `ModerationEventLabel` | Add or negate labels |
| `ModerationEventComment` | Add a moderator comment |
| `ModerationEventMuteReporter` | Mute a reporter |
| `ModerationEventEmail` | Send a moderation email |
| `ModerationEventTag` | Add/remove tags |
| `ModerationEventAcknowledge` | Acknowledge a report |
| `ModerationEventEscalate` | Escalate for review |
| `ModerationEventReverseTakedown` | Reverse a takedown |
| `ModerationEventDivert` | Divert a report |

## Review States

| State | Description |
|-------|-------------|
| `SubjectReviewState.Open` | Under active review |
| `SubjectReviewState.Escalated` | Escalated for higher-level review |
| `SubjectReviewState.Closed` | Review completed |
| `SubjectReviewState.None` | Not under review |

## Next Steps

- [Labeler Services](labeler.md) — Custom label definitions and labeler service support
- [API Reference](api-reference.md) — Complete Ozone client methods
