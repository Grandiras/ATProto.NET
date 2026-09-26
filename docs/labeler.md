# Labeler Services

ATProto.NET supports labeler service information, custom label definitions, and automatic labeler header management. Access labeler features through `client.Bsky.Labeler`.

For running a labeler, or consuming labels from one, the SDK also [signs](#signing-labels) and
[verifies](#verifying-labels) labels, and [serves](#serving-labels) `queryLabels` and the frames of
`subscribeLabels`.

## Fetching Labeler Services

`GetLabelerServicesResponse.Views` is an `IReadOnlyList<JsonElement>` — the Lexicon returns a union of
`labelerView` and `labelerViewDetailed`, so deserialize each entry into the shape you asked for:

```csharp
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;

var response = await client.Bsky.Labeler.GetServicesAsync(
    dids: [Did.Parse("did:plc:labeler1"), Did.Parse("did:plc:labeler2")],
    detailed: true);

foreach (var view in response.Views)
{
    var detailed = view.Deserialize<LabelerViewDetailed>(AtProtoJsonDefaults.Options)!;

    Console.WriteLine($"Labeler: {detailed.Creator.Handle}");
    Console.WriteLine($"Likes: {detailed.LikeCount}");

    foreach (var labelDef in detailed.Policies.LabelValueDefinitions ?? [])
    {
        Console.WriteLine($"  Label: {labelDef.Identifier}");
        Console.WriteLine($"  Severity: {labelDef.Severity}");
        Console.WriteLine($"  Blurs: {labelDef.Blurs}");
    }
}
```

Without `detailed: true`, deserialize into `LabelerView` instead — it carries no `Policies`.

## Standard Label Values

`StandardLabelValues` holds the label values with a global meaning
(`com.atproto.label.defs#labelValue`), plus a few that Bluesky's moderation service applies:

```csharp
using ATProtoNet.Lexicon.App.Bsky.Labeler;

// System labels: clients apply them whatever the viewer's settings
StandardLabelValues.Hide              // "!hide"
StandardLabelValues.Warn              // "!warn"
StandardLabelValues.NoUnauthenticated // "!no-unauthenticated"

// Content and account labels
StandardLabelValues.Porn
StandardLabelValues.Sexual
StandardLabelValues.Nudity
StandardLabelValues.GraphicMedia
StandardLabelValues.Bot

// Applied by Bluesky's moderation service
StandardLabelValues.Spam
StandardLabelValues.Impersonation
StandardLabelValues.Misleading
```

`Gore`, `ContentWarning` and `NotAvailable` are obsolete: none is a global label value, and
`NotAvailable` was always the same string as `NoUnauthenticated`.

## Custom Label Definitions

Labeler services define their own labels with `LabelValueDefinition`. `Identifier`, `Severity`,
`Blurs`, and `Locales` are required; `DefaultSetting` and `AdultOnly` are optional:

```csharp
var labelDef = new LabelValueDefinition
{
    Identifier = "custom-warning",
    Severity = LabelSeverity.Inform,
    Blurs = LabelBlurs.None,
    DefaultSetting = LabelDefaultSetting.Warn,
    AdultOnly = false,
    Locales =
    [
        new LabelValueDefinitionStrings
        {
            Lang = "en",
            Name = "Custom Warning",
            Description = "Content that may need additional context",
        },
    ],
};
```

### Severity Levels

| Constant | Description |
|----------|-------------|
| `LabelSeverity.Inform` | Informational label |
| `LabelSeverity.Alert` | Alert-level label |
| `LabelSeverity.None` | No severity |

### Blur Behavior

| Constant | Description |
|----------|-------------|
| `LabelBlurs.Content` | Blur the entire content |
| `LabelBlurs.Media` | Blur only media |
| `LabelBlurs.None` | No blur applied |

### Default Setting

| Constant | Description |
|----------|-------------|
| `LabelDefaultSetting.Warn` | Show warning by default |
| `LabelDefaultSetting.Hide` | Hide content by default |
| `LabelDefaultSetting.Ignore` | Ignore label by default |

## Labeler Headers

AT Protocol uses the `atproto-accept-labelers` header to declare which labeler services a client subscribes to. ATProto.NET manages this automatically:

### Set Labelers

```csharp
// Subscribe to specific labeler services
client.SetLabelers(["did:plc:labeler1", "did:plc:labeler2"]);
```

The header goes out on every call, authenticated or not, so public AppView reads get the labels
too. To choose labelers for a single call on a shared client instead, pass
`new XrpcCallOptions { AcceptLabelers = [...] }` to `QueryAsync`.

### Clear Labelers

```csharp
client.ClearLabelers();
```

When labelers are set, the `atproto-accept-labelers` header is automatically included in all XRPC requests, causing the server to include labels from those services in its responses.

## Labeler Service Record

Declare your own labeler service:

```csharp
var labelerRecord = new LabelerServiceRecord
{
    CreatedAt = AtDatetime.Now(),
    Policies = new LabelerPolicies
    {
        LabelValues = ["spam", "impersonation"],
        LabelValueDefinitions =
        [
            new LabelValueDefinition
            {
                Identifier = "custom-label",
                Severity = LabelSeverity.Alert,
                Blurs = LabelBlurs.Content,
                DefaultSetting = LabelDefaultSetting.Warn,
                Locales =
                [
                    new LabelValueDefinitionStrings
                    {
                        Lang = "en",
                        Name = "Custom Label",
                        Description = "A custom content label",
                    },
                ],
            },
        ],
    },
};
```

## Signing Labels

Every label a service hands to another carries a signature, as the
[label spec](https://atproto.com/specs/label) requires: the label's DRISL (deterministic
DAG-CBOR) encoding without `sig`, hashed with SHA-256 and signed with the key the labeler's DID
document publishes as `#atproto_label`. `LabelSigner` does this, with the same low-S signatures
repository commits use:

```csharp
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Labeling;
using ATProtoNet.Lexicon.App.Bsky.Labeler;
using ATProtoNet.Models;

using var labelKey = AtProtoCrypto.ImportPrivateKey(pkcs8Bytes, KeyCurve.K256);
var signer = new LabelSigner(Did.Parse("did:plc:mylabeler"), labelKey);

// A new label, stamped with the current time
var label = signer.Sign("at://did:plc:alice/app.bsky.feed.post/3l6oveex3ii2l", "spam");

// Retract it later
var negation = signer.Sign("at://did:plc:alice/app.bsky.feed.post/3l6oveex3ii2l", "spam", negate: true);

// Or sign a label you built (or loaded) yourself; its src must be the signer's DID
var signed = signer.Sign(new Label
{
    Src = signer.Labeler,
    Uri = "did:plc:bob",
    Val = StandardLabelValues.Bot,
    Cts = AtDatetime.Now(),
    Exp = AtDatetime.FromDateTimeOffset(DateTimeOffset.UtcNow.AddDays(30)),
});
```

A signed label has `ver` set to 1 and `sig` to the signature. As `@atproto/ozone` does, a `neg`
of `false` is dropped before signing, and fields the Lexicon does not declare are not carried
over. `LabelSigner` is safe for concurrent use; register one per labeler. The key stays yours to
dispose.

Publish `signer.SigningKey` (a `did:key`) as the labeler's `atproto_label` verification method, or
nothing will verify; a `did:plc` PLC operation's `verificationMethods` takes the `did:key` as it
is. The spec asks a labeler to store each signature with
the key that made it: when you rotate the key, labels signed with the old one stop verifying until
you re-sign them.

`LabelSigning.GetSigningBytes(label)` returns the exact bytes a signature covers, which is useful
when comparing against another implementation.

## Verifying Labels

A service that receives labels from another should verify them. `LabelVerifier` resolves each
label's `src` and checks the signature against its `#atproto_label` key. When the check fails
against a cached document, it refetches the document once, in case the labeler rotated its key:

```csharp
using ATProtoNet.Labeling;

var verifier = new LabelVerifier(resolver); // an IDidResolver; use a caching one

var result = await verifier.VerifyAsync(label);
if (result.IsValid)
    Apply(result.Label);
else
    logger.LogWarning("Label from {Src} did not verify: {Status}", label.Src, result.Status);
```

Verification never throws for a bad label; only cancellation escapes. The `Status` says why a label
failed:

| Status | Meaning |
|--------|---------|
| `Valid` | The signature verifies against the issuer's `#atproto_label` key |
| `Unsigned` | No `sig` |
| `UnsupportedVersion` | `ver` is missing or not 1 |
| `Malformed` | A required field is missing, so nothing can be signed |
| `NoLabelKey` | The issuer publishes no usable `#atproto_label` key, even after a refetch |
| `IssuerUnresolved` | The issuer's DID did not resolve; `result.Error` says why |
| `InvalidSignature` | The signature does not verify against the current key, even after a refetch |

A label is never checked against the labeler's `#atproto` account key: a key published for one
purpose does not sign for another. To check against a key you already have, without resolving,
call `LabelSigning.Verify(label, didKey)`.

A valid signature means the label is authentic, not that it still applies: check `Exp`, and whether
a later negation retracted it.

### Verifying `queryLabels` pages

`VerifyAllAsync` verifies a page and keeps the order:

```csharp
var page = await client.Label.QueryLabelsAsync(["at://did:plc:alice/*"]);
foreach (var result in await verifier.VerifyAllAsync(page.Labels))
{
    if (result.IsValid)
        Apply(result.Label);
}
```

### Verifying a label stream

Give `LabelStreamConsumer` a verifier, and each `LabelsEvent` carries `Verification`, one result per
label in the same order. Labels that fail are still delivered, so you decide what to do with them:

```csharp
var consumer = new LabelStreamConsumer(new LabelStreamConsumerOptions
{
    ServiceUrl = "wss://mod.bsky.app",
    CursorStore = cursorStore,
    Verifier = new LabelVerifier(resolver),
});

await foreach (var message in consumer.ConsumeAsync(cancellationToken: stoppingToken))
{
    if (message is LabelsEvent batch)
    {
        foreach (var result in batch.Verification!)
        {
            if (result.IsValid)
                Apply(result.Label);
        }
    }
}
```

Verification runs as each event is read. A caching resolver keeps that cheap: the labels of a stream
almost always share one issuer, and a refetch is rate-limited per DID.

## Serving Labels

### `queryLabels`

`ATProtoNet.Server` serves `com.atproto.label.queryLabels` through the
[XRPC endpoint hosting](xrpc-handlers.md). Implement `ILabelSource` over your label storage and
register the endpoint:

```csharp
using ATProtoNet.Labeling;
using ATProtoNet.Server.Labeling;
using ATProtoNet.Server.Xrpc;

builder.Services.AddSingleton<ILabelSource, MyLabelSource>();
builder.Services.AddSingleton(new LabelSigner(labelerDid, labelKey)); // optional
builder.Services.AddXrpcEndpoint<QueryLabelsEndpoint>();

var app = builder.Build();
app.MapXrpcEndpoints();
```

The endpoint validates the request before your source sees it: every pattern is an exact subject,
a prefix ending in `*`, or `*` alone (a `*` anywhere else answers `InvalidRequest`), and `limit`
defaults to 50 and is capped at 250. Your source receives a `LabelQuery` and returns a
`QueryLabelsResponse` page with an opaque cursor. A database-backed source translates the patterns
into its own query (a prefix pattern is a `LIKE 'prefix%'`); one that keeps labels in memory can
call `LabelQuery.Matches(label)`, which applies the patterns and sources:

```csharp
public sealed class InMemoryLabelSource : ILabelSource
{
    private readonly List<Label> _labels = []; // in the order they were issued
    private readonly Lock _lock = new();

    public void Add(Label label)
    {
        lock (_lock)
            _labels.Add(label);
    }

    public Task<QueryLabelsResponse> QueryLabelsAsync(LabelQuery query, CancellationToken ct = default)
    {
        // The cursor is the position after the last label served.
        var start = query.Cursor is null ? 0 : int.Parse(query.Cursor, CultureInfo.InvariantCulture);

        lock (_lock)
        {
            var page = _labels
                .Select((label, position) => (label, position))
                .Skip(start)
                .Where(entry => query.Matches(entry.label))
                .Take(query.Limit)
                .ToList();

            return Task.FromResult(new QueryLabelsResponse
            {
                Labels = [.. page.Select(entry => entry.label)],
                Cursor = page.Count == query.Limit
                    ? (page[^1].position + 1).ToString(CultureInfo.InvariantCulture)
                    : null,
            });
        }
    }
}
```

With a `LabelSigner` registered, the endpoint signs any of the labeler's own labels that the source
returns without a signature. Storing signatures saves signing on every query.

### `subscribeLabels`

The XRPC hosting does not serve WebSocket subscriptions, so hosting the stream is up to you.
`LabelStreamFrames` encodes its frames, header and body together, ready to send as one binary
WebSocket message:

```csharp
app.UseWebSockets();
app.Map("/xrpc/com.atproto.label.subscribeLabels", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var cursor = long.TryParse(context.Request.Query["cursor"], out var c) ? c : (long?)null;

    if (cursor > store.LatestSeq)
    {
        var error = LabelStreamFrames.EncodeError(EventStreamErrors.FutureCursor, "Cursor in the future.");
        await socket.SendAsync(error, WebSocketMessageType.Binary, true, context.RequestAborted);
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
        return;
    }

    await foreach (var (seq, labels) in store.ReadFromAsync(cursor, context.RequestAborted))
    {
        var frame = LabelStreamFrames.Encode(new LabelsEvent { Seq = seq, Labels = labels });
        await socket.SendAsync(frame, WebSocketMessageType.Binary, true, context.RequestAborted);
    }
});
```

`Encode` refuses an unsigned label: sign every label before it goes out. An `OutdatedCursor`
notice is `LabelStreamFrames.Encode(new LabelInfoEvent { Name = "OutdatedCursor" })`. Sequencing,
backfill from a cursor, and dropping a consumer that falls too far behind are the labeler's own.

## Next Steps

- [Ozone Moderation](ozone.md) — Full moderation toolkit
- [Service Authentication](crypto.md) — Service auth JWT for labeler services
- [API Reference](api-reference.md) — Complete LabelerClient methods
