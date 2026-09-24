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

Identifiers are typed (`Did`, `Handle`, `AtUri`, `Cid` and `AtDatetime` from `ATProtoNet.Identity`):
parse literals with `Did.Parse("…")` and friends. The one exception is the `subject` filter of
`QueryEventsAsync` and `QuerySubjectsAsync`, a plain string because it takes either an account's DID
or a record's AT URI.

## Moderation

### Emit Moderation Events

```csharp
// Take down content
await client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
{
    Event = new ModEventTakedown
    {
        Comment = "Violates community guidelines",
    },
    Subject = new RepoSubject { Did = Did.Parse("did:plc:abc123") },
    CreatedBy = client.Did!,
});

// Apply a label
await client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
{
    Event = new ModEventLabel
    {
        CreateLabelVals = ["spam"],
        NegateLabelVals = [],
        Comment = "Spam account",
    },
    Subject = new RepoSubject { Did = Did.Parse("did:plc:abc123") },
    CreatedBy = client.Did!,
});

// Add a comment
await client.Ozone.Moderation.EmitEventAsync(new EmitEventRequest
{
    Event = new ModEventComment
    {
        Comment = "Reviewing this account",
    },
    Subject = new RepoSubject { Did = Did.Parse("did:plc:abc123") },
    CreatedBy = client.Did!,
});
```

### Query Events

```csharp
var events = await client.Ozone.Moderation.QueryEventsAsync(
    subject: "did:plc:abc123",
    createdAfter: AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(-7)),
    limit: 50);

foreach (var evt in events.Events)
{
    Console.WriteLine($"Event: {evt.Event} at {evt.CreatedAt}");
}

// Every matching event, fetching pages as needed
await foreach (var evt in client.Ozone.Moderation.EnumerateEventsAsync(subject: "did:plc:abc123"))
{
    Console.WriteLine($"{evt.Id} by {evt.CreatedBy}");
}
```

### Query Subjects Under Review

```csharp
var subjects = await client.Ozone.Moderation.QuerySubjectsAsync(limit: 25);

foreach (var subject in subjects.Subjects)
{
    Console.WriteLine($"Subject: {subject.Subject}");
    Console.WriteLine($"Review state: {subject.ReviewState}");
}

// The whole escalated queue
await foreach (var subject in client.Ozone.Moderation.EnumerateSubjectsAsync(
    reviewState: SubjectReviewState.Escalated))
{
    Console.WriteLine(subject.Subject);
}
```

### Get Moderation Record/Repo Info

```csharp
var record = await client.Ozone.Moderation.GetRecordAsync(
    uri: AtUri.Parse("at://did:plc:abc/app.bsky.feed.post/123"));
var repo = await client.Ozone.Moderation.GetRepoAsync(did: Did.Parse("did:plc:abc123"));
```

### Search Repositories

```csharp
var results = await client.Ozone.Moderation.SearchReposAsync(q: "spam");

await foreach (var repo in client.Ozone.Moderation.EnumerateReposAsync(q: "spam"))
    Console.WriteLine(repo.Handle);
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
await client.Ozone.Communication.DeleteTemplateAsync(id: template.Id);
```

## Team Management

```csharp
// Add a team member
await client.Ozone.Team.AddMemberAsync(new AddMemberRequest
{
    Did = Did.Parse("did:plc:newmoderator"),
    Role = TeamMemberRole.Moderator,
});

// List team members, one page or all of them
var members = await client.Ozone.Team.ListMembersAsync();
await foreach (var member in client.Ozone.Team.EnumerateMembersAsync())
    Console.WriteLine($"{member.Did}: {member.Role}");

// Update a member's role
await client.Ozone.Team.UpdateMemberAsync(new UpdateMemberRequest
{
    Did = Did.Parse("did:plc:newmoderator"),
    Role = TeamMemberRole.Admin,
});

// Remove a team member
await client.Ozone.Team.DeleteMemberAsync(did: Did.Parse("did:plc:newmoderator"));
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
await client.Ozone.Set.AddValuesAsync(
    "blocked-domains",
    ["spam-site.example.com", "bad-domain.example.com"]);

// Get one page of a set's values, or all of them
var values = await client.Ozone.Set.GetValuesAsync(name: "blocked-domains");
await foreach (var value in client.Ozone.Set.EnumerateValuesAsync("blocked-domains"))
    Console.WriteLine(value);

// Query sets, one page or all of them
var sets = await client.Ozone.Set.QuerySetsAsync();
await foreach (var set in client.Ozone.Set.EnumerateSetsAsync())
    Console.WriteLine($"{set.Name}: {set.SetSize} values");

// Remove values
await client.Ozone.Set.DeleteValuesAsync(
    "blocked-domains",
    ["spam-site.example.com"]);

// Delete a set
await client.Ozone.Set.DeleteSetAsync(name: "blocked-domains");
```

## Signature Analysis

Find related accounts through signature correlation:

```csharp
// Find correlated signatures
var correlation = await client.Ozone.Signature.FindCorrelationAsync(
    dids: [Did.Parse("did:plc:abc"), Did.Parse("did:plc:def")]);

// Search accounts by signature
var accounts = await client.Ozone.Signature.SearchAccountsAsync(
    values: [new SigDetail { Property = "userAgent", Value = "some-signal" }]);

// Find related accounts
var related = await client.Ozone.Signature.FindRelatedAccountsAsync(did: Did.Parse("did:plc:abc123"));
```

## Server Configuration

```csharp
var config = await client.Ozone.Server.GetConfigAsync();
```

## Moderation Event Types

| Event Type | Description |
|------------|-------------|
| `ModEventTakedown` | Take down content or account |
| `ModEventLabel` | Add or negate labels |
| `ModEventComment` | Add a moderator comment |
| `ModEventMuteReporter` | Mute a reporter |
| `ModEventEmail` | Send a moderation email |
| `ModEventTag` | Add/remove tags |
| `ModEventAcknowledge` | Acknowledge a report |
| `ModEventEscalate` | Escalate for review |
| `ModEventReverseTakedown` | Reverse a takedown |
| `ModEventDivert` | Divert a report |
| `ModEventReport` | Record a report against the subject |
| `ModEventMute` / `ModEventUnmute` | Mute or unmute a subject |
| `ModEventUnmuteReporter` | Unmute a reporter |

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
