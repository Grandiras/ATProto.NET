# Ozone Moderation

ATProto.NET provides full support for the `tools.ozone.*` namespace — the content moderation toolkit used by Bluesky. Access it through `client.Ozone`.

## Sub-Clients

| Client | Namespace | Description |
|--------|-----------|-------------|
| `Ozone.Moderation` | `tools.ozone.moderation` | Moderation events, subject review, lookups, scheduled actions |
| `Ozone.Report` | `tools.ozone.report` | Individual reports: query, assign, activities, close, statistics |
| `Ozone.Queue` | `tools.ozone.queue` | Moderation queues, report routing, queue assignments |
| `Ozone.Communication` | `tools.ozone.communication` | Email templates |
| `Ozone.Team` | `tools.ozone.team` | Team member management |
| `Ozone.Set` | `tools.ozone.set` | Named sets of values (DIDs, URIs) |
| `Ozone.Setting` | `tools.ozone.setting` | Instance and personal settings |
| `Ozone.Safelink` | `tools.ozone.safelink` | URL safety rules and their audit log |
| `Ozone.Verification` | `tools.ozone.verification` | Verifications the Ozone service issues |
| `Ozone.Hosting` | `tools.ozone.hosting` | Account history from the account's host |
| `Ozone.Server` | `tools.ozone.server` | Server configuration |
| `Ozone.Signature` | `tools.ozone.signature` | Signature correlation & analysis |

Identifiers are typed (`Did`, `Handle`, `AtUri`, `Cid`, `Nsid` and `AtDatetime` from
`ATProtoNet.Identity`): parse literals with `Did.Parse("…")` and friends. The exception is a
subject that may be either an account's DID or a record's AT URI, a plain string: the `subject`
filter of `QueryEventsAsync`, `SubjectStatusFilter` and `ReportFilter`, `GetSubjectsAsync` and
`CloseReportsAsync`.

Subjects (`RepoSubject`, `RecordSubject`, `MessageSubject`, `ConvoSubject`) are the variants of the
one `ModerationSubject` union in `ATProtoNet.Lexicon.Com.AtProto.Moderation`, which reports and a
PDS's subject status use too; import that namespace alongside `ATProtoNet.Lexicon.Tools.Ozone.Moderation`.

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

### Query the Review Queue

`tools.ozone.moderation.queryStatuses` takes some 35 filters, so `QueryStatusesAsync` takes them as
one `SubjectStatusFilter`:

```csharp
var page = await client.Ozone.Moderation.QueryStatusesAsync(limit: 25);

foreach (var status in page.SubjectStatuses)
{
    Console.WriteLine($"Subject: {status.Subject}");
    Console.WriteLine($"Review state: {status.ReviewState}");
}

// The whole escalated queue, highest priority first
await foreach (var status in client.Ozone.Moderation.EnumerateStatusesAsync(new SubjectStatusFilter
{
    ReviewState = SubjectReviewState.Escalated,
    Takendown = false,
    SortField = "priorityScore",
}))
{
    Console.WriteLine($"{status.Subject}: {status.PriorityScore}");
}
```

### Get Moderation Record/Repo Info

```csharp
var record = await client.Ozone.Moderation.GetRecordAsync(
    uri: AtUri.Parse("at://did:plc:abc/app.bsky.feed.post/123"));
var repo = await client.Ozone.Moderation.GetRepoAsync(did: Did.Parse("did:plc:abc123"));
```

Several at once, up to 100 per call. Each entry is a `ModerationSubjectView`: the detail view, or
a `RepoViewNotFound` / `RecordViewNotFound` for a subject Ozone does not know.

```csharp
var repos = await client.Ozone.Moderation.GetReposAsync([Did.Parse("did:plc:abc"), Did.Parse("did:plc:def")]);
foreach (var entry in repos.Repos)
{
    switch (entry)
    {
        case RepoViewDetail repo:
            Console.WriteLine($"{repo.Handle}: {repo.Moderation.SubjectStatus?.ReviewState}");
            break;
        case RepoViewNotFound missing:
            Console.WriteLine($"{missing.Did}: not known to Ozone");
            break;
    }
}

// Status, account, profile and record in one call, for DIDs and AT URIs alike
var subjects = await client.Ozone.Moderation.GetSubjectsAsync(
    ["did:plc:abc", "at://did:plc:abc/app.bsky.feed.post/123"]);

// An account's history, day by day, and how its reports turned out
var timeline = await client.Ozone.Moderation.GetAccountTimelineAsync(Did.Parse("did:plc:abc"));
var reporterStats = await client.Ozone.Moderation.GetReporterStatsAsync([Did.Parse("did:plc:abc")]);

// The account's private app preferences (typed, as in client.Bsky.Actor)
var preferences = await client.Ozone.Moderation.GetAccountPreferencesAsync(Did.Parse("did:plc:abc"));
```

### Search Repositories

```csharp
var results = await client.Ozone.Moderation.SearchReposAsync(q: "spam");

await foreach (var repo in client.Ozone.Moderation.EnumerateReposAsync(q: "spam"))
    Console.WriteLine(repo.Handle);
```

### Scheduled Actions

Schedule a takedown to run later, at an exact time or at a random time in a window:

```csharp
var scheduled = await client.Ozone.Moderation.ScheduleActionAsync(
    [Did.Parse("did:plc:abc"), Did.Parse("did:plc:def")],
    new ScheduledTakedown { Comment = "Spam network", Policies = ["spam"] },
    new SchedulingConfig
    {
        ExecuteAfter = AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(1)),
        ExecuteUntil = AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(6)),
    },
    createdBy: client.Did!);

foreach (var failure in scheduled.Failed)
    Console.WriteLine($"{failure.Subject}: {failure.Error}");

// Pending actions, then cancel them for one account
await foreach (var action in client.Ozone.Moderation.EnumerateScheduledActionsAsync([ScheduledActionStatus.Pending]))
    Console.WriteLine($"{action.Did}: {action.Action} at {action.ExecuteAt}");

await client.Ozone.Moderation.CancelScheduledActionsAsync([Did.Parse("did:plc:abc")], "Appeal granted");
```

## Reports

`tools.ozone.report` works on individual reports, where the subject status (above) aggregates every
report on a subject. A report moves through `ReportStatus` values (`open`, `queued`, `assigned`,
`escalated`, `closed`) as queues, moderators and activities act on it.

### Query Reports

`QueryReportsAsync` takes the status, plus the other filters as one `ReportFilter`:

```csharp
var page = await client.Ozone.Report.QueryReportsAsync(ReportStatus.Open, limit: 25);

// Every open spam report on posts, oldest first
await foreach (var report in client.Ozone.Report.EnumerateReportsAsync(ReportStatus.Open, new ReportFilter
{
    ReportTypes = [ReportReasons.MisleadingSpam, ReportReasons.Spam],
    SubjectType = ReportSubjectType.Record,
    Collections = [Nsid.Parse("app.bsky.feed.post")],
    SortDirection = "asc",
}))
{
    Console.WriteLine($"#{report.Id} {report.ReportType} on {report.Subject.Subject} by {report.ReportedBy}");
}

var one = await client.Ozone.Report.GetReportAsync(42);
var latest = await client.Ozone.Report.GetLatestReportAsync();
```

`ReportReasons` has both sets of reasons: the original coarse `com.atproto.moderation.defs#reason*`
ones (`Spam`, `Violation`, `Misleading`, …) and the granular `tools.ozone.report.defs#reason*` ones
upstream now prefers, grouped by category (`ViolenceThreats`, `SexualNcii`, `ChildSafetyCsam`,
`HarassmentDoxxing`, `MisleadingImpersonation`, `RuleBanEvasion`, `SelfHarmED`, …). Each coarse
reason's documentation names its granular replacement.

### Assign and Record Activity

```csharp
// Take a report (the caller by default; admins may assign anyone)
var assignment = await client.Ozone.Report.AssignModeratorAsync(42);

// Record what happened. A status-changing activity moves the report in the same step.
await client.Ozone.Report.CreateActivityAsync(42, new EscalationActivity(), internalNote: "Needs policy review");
await client.Ozone.Report.CreateActivityAsync(42, new NoteActivity(), publicNote: "Thanks, we're looking into it");

// Or address the report by the moderation event that created it
await client.Ozone.Report.CreateActivityForEventAsync(eventId: 7, new CloseActivity());

// One report's history, most recent first
await foreach (var activity in client.Ozone.Report.EnumerateReportActivitiesAsync(42))
    Console.WriteLine($"{activity.CreatedAt} {activity.Activity.GetType().Name} by {activity.CreatedBy}");

// Every activity across reports, for a poller
var recent = await client.Ozone.Report.QueryActivitiesAsync(
    activityTypes: ["closeActivity"],
    createdAfter: AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddHours(-1)));

await client.Ozone.Report.UnassignModeratorAsync(42);
```

### Close Reports Without Action

For automated flows that resolve reports without acting on the subject. Reports whose status cannot
move to `closed` are skipped.

```csharp
var closed = await client.Ozone.Report.CloseReportsAsync(
    "at://did:plc:abc/app.bsky.feed.post/123",
    reportTypes: [ReportReasons.MisleadingSpam],
    internalNote: "Resolved by the spam classifier",
    isAutomated: true);
Console.WriteLine($"Closed {closed.ClosedCount} reports");
```

### Statistics

```csharp
// Today, overall or for one queue (-1 for reports in no queue), moderator or report type
var live = await client.Ozone.Report.GetLiveStatsAsync(queueId: 4);
Console.WriteLine($"{live.Stats.PendingCount} pending, {live.Stats.ActionRate}% actioned");

// Daily snapshots, newest first
await foreach (var day in client.Ozone.Report.EnumerateHistoricalStatsAsync(
    startDate: AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(-30))))
{
    Console.WriteLine($"{day.Date}: {day.InboundCount} in, {day.ActionedCount} closed");
}

// Recompute a range of days after a failure
await client.Ozone.Report.RefreshStatsAsync(new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 31));
```

## Queues

A queue collects the reports that match its criteria: subject types, a collection for record
subjects, and report types. Ozone's queue router fills it; a queue without criteria only gets
reports sent to it by hand (`modTool.meta.queueId` on an emitted event, or `ReassignQueueAsync`).

```csharp
var created = await client.Ozone.Queue.CreateQueueAsync(
    "Post spam",
    subjectTypes: [ReportSubjectType.Record],
    collection: Nsid.Parse("app.bsky.feed.post"),
    reportTypes: [ReportReasons.MisleadingSpam, ReportReasons.Spam],
    recommendedPolicies: ["spam"]);

await foreach (var queue in client.Ozone.Queue.EnumerateQueuesAsync(enabled: true))
    Console.WriteLine($"{queue.Name}: {queue.Stats.PendingCount} pending");

await client.Ozone.Queue.UpdateQueueAsync(created.Queue.Id, enabled: false);

// Move one report, or route a range of reports again after changing the criteria
await client.Ozone.Report.ReassignQueueAsync(reportId: 42, queueId: created.Queue.Id);
var routed = await client.Ozone.Queue.RouteReportsAsync(startReportId: 1000, endReportId: 1999);

// Who works which queue
await client.Ozone.Queue.AssignModeratorAsync(created.Queue.Id, Did.Parse("did:plc:mod"));
await foreach (var assignment in client.Ozone.Queue.EnumerateAssignmentsAsync(queueIds: [created.Queue.Id]))
    Console.WriteLine($"{assignment.Did} since {assignment.StartAt}");

// Delete it, moving its reports to another queue (or to none)
await client.Ozone.Queue.DeleteQueueAsync(created.Queue.Id, migrateToQueueId: 5);
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

// Search accounts that share any of these signature values
var accounts = await client.Ozone.Signature.SearchAccountsAsync(values: ["some-signal"]);

// Find related accounts
var related = await client.Ozone.Signature.FindRelatedAccountsAsync(did: Did.Parse("did:plc:abc123"));
```

## Settings

Key-value settings under NSID keys, for the whole instance or for one moderator:

```csharp
await client.Ozone.Setting.UpsertOptionAsync(
    Nsid.Parse("tools.ozone.setting.client.queueOrder"),
    SettingScope.Instance,
    JsonSerializer.SerializeToElement(new { order = new[] { "spam", "harassment" } }),
    managerRole: TeamMemberRole.Admin);

await foreach (var option in client.Ozone.Setting.EnumerateOptionsAsync(SettingScope.Instance, prefix: "tools.ozone.setting.client"))
    Console.WriteLine($"{option.Key} = {option.Value}");

await client.Ozone.Setting.RemoveOptionsAsync([Nsid.Parse("tools.ozone.setting.client.queueOrder")], SettingScope.Instance);
```

## URL Safety Rules

Rules that block, warn on or allow links to a URL or a whole domain, with an audit log:

```csharp
await client.Ozone.Safelink.AddRuleAsync(
    "scam.example.com", SafelinkPatternType.Domain, SafelinkActionType.Block, SafelinkReasonType.Phishing,
    comment: "Credential phishing");

await foreach (var rule in client.Ozone.Safelink.EnumerateRulesAsync(actions: [SafelinkActionType.Block]))
    Console.WriteLine($"{rule.Url} ({rule.Pattern}): {rule.Reason}");

await client.Ozone.Safelink.UpdateRuleAsync(
    "scam.example.com", SafelinkPatternType.Domain, SafelinkActionType.Warn, SafelinkReasonType.Phishing);
await client.Ozone.Safelink.RemoveRuleAsync("scam.example.com", SafelinkPatternType.Domain, "False positive");

await foreach (var change in client.Ozone.Safelink.EnumerateEventsAsync(urls: ["scam.example.com"]))
    Console.WriteLine($"{change.CreatedAt} {change.EventType} by {change.CreatedBy}");
```

## Verifications

When the Ozone service is a trusted verifier, it issues and revokes verifications in batches of up
to 100. A verification is bound to the account's handle and display name at the time:

```csharp
var granted = await client.Ozone.Verification.GrantVerificationsAsync(
[
    new VerificationInput
    {
        Subject = Did.Parse("did:plc:abc"),
        Handle = Handle.Parse("alice.example.com"),
        DisplayName = "Alice",
    },
]);

await foreach (var verification in client.Ozone.Verification.EnumerateVerificationsAsync(isRevoked: false))
    Console.WriteLine($"{verification.Handle} verified by {verification.Issuer}");

await client.Ozone.Verification.RevokeVerificationsAsync(
    granted.Verifications.Select(v => v.Uri), revokeReason: "Handle changed");
```

## Account History

What the account's host recorded: creation, email and handle changes, email confirmation, password
changes.

```csharp
await foreach (var entry in client.Ozone.Hosting.EnumerateAccountHistoryAsync(
    Did.Parse("did:plc:abc"), events: [AccountHistoryEventType.HandleUpdated, AccountHistoryEventType.EmailUpdated]))
{
    var change = entry.Details switch
    {
        HandleUpdated handle => $"handle → {handle.Handle}",
        EmailUpdated email => $"email → {email.Email}",
        _ => entry.Details.GetType().Name,
    };
    Console.WriteLine($"{entry.CreatedAt}: {change}");
}
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
| `ModEventResolveAppeal` | Resolve an appeal |
| `ModEventPriorityScore` | Set the subject's priority score in the review queue |
| `AgeAssuranceOverrideEvent` / `AgeAssurancePurgeEvent` | Override or purge the account's age-assurance state |
| `RevokeAccountCredentialsEvent` | Revoke the account's sessions and app passwords |

Ozone also records events on its own, which read back from `QueryEventsAsync` and `GetEventAsync`:
`AccountEvent`, `IdentityEvent` and `RecordEvent` (changes seen on the network), `AgeAssuranceEvent`
(the app view's age-assurance flow), and `ScheduleTakedownEvent` / `CancelScheduledTakedownEvent`
(from scheduled actions). An event type this SDK does not model reads as `UnknownModEvent`, so a
`switch` over events needs a default arm.

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
