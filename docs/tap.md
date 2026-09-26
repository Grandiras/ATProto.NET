# Tap

[Tap](https://github.com/bluesky-social/indigo/tree/main/cmd/tap) is Bluesky's Sync 1.1 consumer and backfill service. It connects to the relay's firehose, verifies every commit (signatures, MST inversion, per-repository chains), backfills the repositories you ask for from their PDS, filters collections, and hands your application plain JSON events: one per record created, updated or deleted, and one per identity or account status change. Your application needs no CBOR, no cryptography and no cursor bookkeeping.

`ATProtoNet.Tap.TapClient` talks to a Tap instance: its `/channel` WebSocket of events with acknowledgements, and its admin endpoints. `ATProtoNet.Server` receives Tap's webhook delivery mode. The wire protocol and the admin auth are those of the reference TypeScript client, [`@atproto/tap`](https://github.com/bluesky-social/atproto/tree/main/packages/tap).

If you would rather verify the firehose in process, see [Verifying the firehose](firehose.md#verifying-the-firehose-sync-11): `TypedFirehoseConsumer` with a `RepoSyncVerifier` runs the same checks.

## Running Tap

```bash
go run github.com/bluesky-social/indigo/cmd/tap@latest run
# SQLite at ./tap.db, listening on :2480, following relay1.us-east.bsky.network
```

Set `TAP_ADMIN_PASSWORD` to require HTTP Basic auth (user `admin`) on every request, `TAP_COLLECTION_FILTERS` to narrow record events to some collections (`app.bsky.feed.post,app.bsky.graph.*`), `TAP_SIGNAL_COLLECTION` or `TAP_FULL_NETWORK` to track repositories without adding them by hand, and `TAP_WEBHOOK_URL` to have events POSTed rather than streamed. See Tap's README for the rest.

## Reading events

```csharp
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Feed;       // PostRecord
using ATProtoNet.Lexicon.Com.AtProto.Sync;    // RepoOpAction
using ATProtoNet.Tap;

using var tap = new TapClient(new Uri("http://localhost:2480"), adminPassword: "secret");

// Track a repository: Tap backfills it, then streams its live changes.
await tap.AddReposAsync([Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz")]);

var channel = tap.OpenChannel();
await foreach (var evt in channel.ReadAllAsync(stoppingToken))
{
    switch (evt)
    {
        case TapRecordEvent record:
            if (record.Operation == RepoOpAction.Delete)
                await index.RemoveAsync(record.Uri);
            else
                await index.UpsertAsync(record.Uri, record.Cid, record.GetRecord<PostRecord>());
            break;

        case TapIdentityEvent identity:
            await accounts.UpdateAsync(identity.Did, identity.Handle, identity.IsActive, identity.Status);
            break;
    }

    // Acknowledge once the event is processed: Tap sends it again until you do.
    await channel.AckAsync(evt);
}
```

`TapRecordEvent` implements `IRecordEvent`, like the firehose's `FirehoseRecordEvent` and Jetstream's `JetstreamCommitEvent`, so the same indexing code can take all three. Its `Live` is `true` for a change from the firehose and `false` for one from a backfill or resync.

### Delivery and acknowledgements

Tap delivers each event **at least once**, and resends it after a timeout (`TAP_RETRY_TIMEOUT`, 60 seconds by default) until it is acknowledged. Acknowledge an event once it is processed; leave a failed one unacknowledged to have Tap retry it. Processing must be idempotent.

Tap keeps each repository's events in order: a live event waits until everything before it is acknowledged, and nothing after it is sent until it is. So every event has to be acknowledged sooner or later, or its repository stalls. There is no global order across repositories.

`AckAsync` sends `{"type":"ack","id":…}` on the connection. An acknowledgement made while the connection is down is sent once the channel reconnects, and acknowledging twice is harmless. With `TAP_DISABLE_ACKS` (fire and forget) Tap ignores acknowledgements.

Several channels may be open at once, from one process or many; Tap shares events out among them, still in order per repository.

### Reconnecting and errors

The channel reconnects when its connection drops, per `TapClientOptions.Reconnect` (the same `StreamReconnectPolicy` the firehose consumers use), until the token is cancelled; cancelling ends the enumeration normally. A Tap instance that refuses the connection outright, such as one in webhook mode or with a different admin password, ends it with an `EventStreamException` carrying the HTTP status.

A message that is not a valid event (malformed, or of a type this SDK version does not model) is reported to `TapClientOptions.OnError` and skipped without being acknowledged, so Tap sends it again.

## Admin endpoints

| Method | Endpoint | |
|---|---|---|
| `AddReposAsync(dids)` | `POST /repos/add` | Track repositories; each is backfilled, then streamed live |
| `RemoveReposAsync(dids)` | `POST /repos/remove` | Stop tracking repositories and delete Tap's metadata for them |
| `ResolveDidAsync(did)` | `GET /resolve/:did` | Resolve a DID through Tap's identity cache; `null` when it does not resolve |
| `GetRepoInfoAsync(did)` | `GET /info/:did` | A tracked repository's `State`, `Rev`, `Records`, last `Error` and `Retries` |

A refused or failed request throws a `TapException` with the `StatusCode` Tap answered with; `GetRepoInfoAsync` for a repository Tap does not track fails with 404.

Every request carries `Authorization: Basic base64("admin:" + password)` when `AdminPassword` is set. `TapClient.FormatAdminAuthHeader(password)` builds the header.

## Webhooks

With `TAP_WEBHOOK_URL` set, Tap POSTs each event to that URL as JSON, with the admin password as HTTP Basic auth, and counts it delivered once the endpoint answers with a 2xx; anything else is retried with backoff. `ATProtoNet.Server` maps the receiving endpoint:

```csharp
using ATProtoNet.Server.Tap;
using ATProtoNet.Tap;

app.MapTapWebhook("/tap/webhook", async (evt, ct) =>
{
    if (evt is TapRecordEvent record)
        await index.ApplyAsync(record, ct);
}, options => options.AdminPassword = builder.Configuration["Tap:AdminPassword"]);
```

Or register a handler class and map it by type; it is resolved from the request's services:

```csharp
builder.Services.AddScoped<IndexingTapHandler>();   // : ITapEventHandler
app.MapTapWebhook<IndexingTapHandler>("/tap/webhook", o => o.AdminPassword = tapPassword);
```

The endpoint:

- checks the `Authorization` header in constant time and answers **401** unless it is Basic auth for user `admin` with the configured password. Mapping one without a password throws, unless `AllowUnauthenticated` is set for an instance that has none;
- refuses a body over `MaxBodyBytes` (4 MiB by default) with **413**, whether declared or streamed;
- acknowledges a body that is not a Tap event (malformed, or of a type this SDK version does not model) with **200**, and reports it to `OnUnreadableEvent` (a warning log by default): Tap resends any refused event forever and holds back that repository's later events meanwhile;
- answers **500** when the handler throws, so Tap retries, and **200** once it returns.

Both methods return the endpoint's `RouteHandlerBuilder`, for conventions such as rate limiting. Tap redelivers an event whose webhook failed or timed out, so handlers must be idempotent.

## Event format

What Tap sends, on the channel and to webhooks:

```json
{"id":12345,"type":"record","record":{"live":true,"did":"did:plc:abc123","rev":"3kb3fge5lm32x","collection":"app.bsky.feed.post","rkey":"3kb3fge5lm32x","action":"create","record":{"$type":"app.bsky.feed.post","text":"Hello world!","createdAt":"2024-10-07T12:00:00.000Z"},"cid":"bafyrei…"}}
{"id":12346,"type":"identity","identity":{"did":"did:plc:abc123","handle":"alice.bsky.social","is_active":true,"status":"active"}}
```

`TapEvent.Parse` reads one, as `parseTapEvent` does in `@atproto/tap`, into a `TapRecordEvent` or `TapIdentityEvent` with typed identifiers. A delete carries neither `record` nor `cid`. `TapIdentityEvent.Status` is one of the `TapRepoStatus` values: `active`, `takendown`, `suspended`, `deactivated` or `deleted`.
