# Labeler Services

ATProto.NET supports labeler service information, custom label definitions, and automatic labeler header management. Access labeler features through `client.Bsky.Labeler`.

## Fetching Labeler Services

```csharp
var response = await client.Bsky.Labeler.GetServicesAsync(
    dids: ["did:plc:labeler1", "did:plc:labeler2"],
    detailed: true);

foreach (var labeler in response.Views)
{
    if (labeler is LabelerViewDetailed detailed)
    {
        Console.WriteLine($"Labeler: {detailed.Creator.Handle}");
        Console.WriteLine($"Likes: {detailed.LikeCount}");

        foreach (var labelDef in detailed.Policies.LabelValueDefinitions ?? [])
        {
            Console.WriteLine($"  Label: {labelDef.Identifier}");
            Console.WriteLine($"  Severity: {labelDef.Severity}");
            Console.WriteLine($"  Blurs: {labelDef.Blurs}");
        }
    }
}
```

## Standard Label Values

The SDK provides constants for all well-known Bluesky label values:

```csharp
using ATProtoNet.Lexicon.Com.AtProto.Label;

// Content labels
StandardLabelValues.Porn
StandardLabelValues.Sexual
StandardLabelValues.Nudity
StandardLabelValues.GraphicMedia
StandardLabelValues.Gore

// Account labels
StandardLabelValues.Spam
StandardLabelValues.Impersonation
```

## Custom Label Definitions

Labeler services define their own labels with `LabelValueDefinition`:

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

## Next Steps

- [Ozone Moderation](ozone.md) — Full moderation toolkit
- [Service Authentication](crypto.md) — Service auth JWT for labeler services
- [API Reference](api-reference.md) — Complete LabelerClient methods
