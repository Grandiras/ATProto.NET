using System.Text.Json;
using ATProtoNet.Lexicon.App.Bsky.Labeler;

namespace ATProtoNet.Tests.Lexicon.App.Bsky.Labeler;

public class LabelerModelsTests
{
    [Fact]
    public void LabelerServiceRecord_Serializes()
    {
        var record = new LabelerServiceRecord
        {
            Policies = new LabelerPolicies
            {
                LabelValues = ["porn", "spam", "custom-label"],
                LabelValueDefinitions =
                [
                    new LabelValueDefinition
                    {
                        Identifier = "custom-label",
                        Severity = LabelSeverity.Alert,
                        Blurs = LabelBlurs.Content,
                        DefaultSetting = LabelDefaultSetting.Warn,
                        AdultOnly = false,
                        Locales =
                        [
                            new LabelValueDefinitionStrings
                            {
                                Lang = "en",
                                Name = "Custom Label",
                                Description = "A custom label for testing",
                            }
                        ]
                    }
                ]
            },
            CreatedAt = "2024-01-01T00:00:00Z",
        };

        var json = JsonSerializer.Serialize(record);
        var doc = JsonDocument.Parse(json);

        Assert.Equal("app.bsky.labeler.service", doc.RootElement.GetProperty("$type").GetString());
        Assert.Equal("2024-01-01T00:00:00Z", doc.RootElement.GetProperty("createdAt").GetString());

        var policies = doc.RootElement.GetProperty("policies");
        var labelValues = policies.GetProperty("labelValues");
        Assert.Equal(3, labelValues.GetArrayLength());
        Assert.Equal("porn", labelValues[0].GetString());

        var defs = policies.GetProperty("labelValueDefinitions");
        Assert.Equal(1, defs.GetArrayLength());
        Assert.Equal("custom-label", defs[0].GetProperty("identifier").GetString());
        Assert.Equal("alert", defs[0].GetProperty("severity").GetString());
        Assert.Equal("content", defs[0].GetProperty("blurs").GetString());
    }

    [Fact]
    public void LabelerViewDetailed_Deserializes()
    {
        var json = """
        {
            "uri": "at://did:plc:labeler1/app.bsky.labeler.service/self",
            "cid": "bafyreia123",
            "creator": { "did": "did:plc:labeler1", "handle": "mod.bsky.social" },
            "likeCount": 42,
            "indexedAt": "2024-01-01T00:00:00Z",
            "policies": {
                "labelValues": ["porn", "spam"],
                "labelValueDefinitions": [
                    {
                        "identifier": "custom",
                        "severity": "inform",
                        "blurs": "none",
                        "defaultSetting": "warn",
                        "locales": [
                            { "lang": "en", "name": "Custom", "description": "Test label" }
                        ]
                    }
                ]
            }
        }
        """;

        var view = JsonSerializer.Deserialize<LabelerViewDetailed>(json);

        Assert.NotNull(view);
        Assert.Equal("at://did:plc:labeler1/app.bsky.labeler.service/self", view.Uri);
        Assert.Equal("bafyreia123", view.Cid);
        Assert.Equal(42, view.LikeCount);
        Assert.NotNull(view.Policies);
        Assert.Equal(2, view.Policies.LabelValues!.Count);
        Assert.Single(view.Policies.LabelValueDefinitions!);
        Assert.Equal("custom", view.Policies.LabelValueDefinitions![0].Identifier);
        Assert.Equal("en", view.Policies.LabelValueDefinitions[0].Locales[0].Lang);
    }

    [Fact]
    public void LabelerView_Deserializes()
    {
        var json = """
        {
            "uri": "at://did:plc:labeler1/app.bsky.labeler.service/self",
            "cid": "bafyreia456",
            "creator": { "did": "did:plc:labeler1" },
            "indexedAt": "2024-06-01T00:00:00Z"
        }
        """;

        var view = JsonSerializer.Deserialize<LabelerView>(json);

        Assert.NotNull(view);
        Assert.Null(view.LikeCount);
        Assert.Null(view.Viewer);
    }

    [Fact]
    public void StandardLabelValues_AreCorrect()
    {
        Assert.Equal("porn", StandardLabelValues.Porn);
        Assert.Equal("sexual", StandardLabelValues.Sexual);
        Assert.Equal("nudity", StandardLabelValues.Nudity);
        Assert.Equal("graphic-media", StandardLabelValues.GraphicMedia);
        Assert.Equal("gore", StandardLabelValues.Gore);
        Assert.Equal("spam", StandardLabelValues.Spam);
        Assert.Equal("impersonation", StandardLabelValues.Impersonation);
    }

    [Fact]
    public void LabelSeverity_Constants()
    {
        Assert.Equal("inform", LabelSeverity.Inform);
        Assert.Equal("alert", LabelSeverity.Alert);
        Assert.Equal("none", LabelSeverity.None);
    }

    [Fact]
    public void LabelBlurs_Constants()
    {
        Assert.Equal("content", LabelBlurs.Content);
        Assert.Equal("media", LabelBlurs.Media);
        Assert.Equal("none", LabelBlurs.None);
    }

    [Fact]
    public void LabelDefaultSetting_Constants()
    {
        Assert.Equal("ignore", LabelDefaultSetting.Ignore);
        Assert.Equal("warn", LabelDefaultSetting.Warn);
        Assert.Equal("hide", LabelDefaultSetting.Hide);
    }

    [Fact]
    public void LabelerViewerState_Deserializes()
    {
        var json = """{ "like": "at://did:plc:user1/app.bsky.feed.like/abc123" }""";
        var viewer = JsonSerializer.Deserialize<LabelerViewerState>(json);

        Assert.NotNull(viewer);
        Assert.Equal("at://did:plc:user1/app.bsky.feed.like/abc123", viewer.Like);
    }
}
