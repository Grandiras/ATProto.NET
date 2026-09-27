using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Communication;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Set;
using ATProtoNet.Lexicon.Tools.Ozone.Team;
using ATProtoNet.Tests.TestSupport;

namespace ATProtoNet.Tests.Ozone;

public sealed class OzoneClientTests : IDisposable
{
    private readonly XrpcTestClient _fixture = new(instanceUrl: "https://ozone.test");

    private AtProtoClient Client => _fixture.Client;

    public void Dispose() => _fixture.Dispose();

    private static string Json(object body) => JsonSerializer.Serialize(body);

    // ─── Moderation ───

    [Fact]
    public async Task EmitEvent_SendsTakedown()
    {
        // Raw JSON to get $type as a property name, which an anonymous object cannot produce.
        _fixture.On("tools.ozone.moderation.emitEvent", """
            {
                "id": 1,
                "event": {
                    "$type": "tools.ozone.moderation.defs#modEventTakedown",
                    "comment": "spam",
                    "durationInHours": 24
                },
                "subject": {
                    "$type": "com.atproto.admin.defs#repoRef",
                    "did": "did:plc:abc"
                },
                "createdBy": "did:plc:mod",
                "createdAt": "2024-01-01T00:00:00Z"
            }
            """);

        var request = new EmitEventRequest
        {
            Event = new ModEventTakedown { Comment = "spam", DurationInHours = 24 },
            Subject = new RepoSubject { Did = Did.Parse("did:plc:abc") },
            CreatedBy = Did.Parse("did:plc:mod"),
        };

        var result = await Client.Ozone.Moderation.EmitEventAsync(request);

        Assert.Equal(1L, result.Id);
        Assert.Equal("did:plc:mod", result.CreatedBy);
    }

    // getEvent, getRepo and the plain form of queryEvents are covered by
    // ATProtoNet.Tests.Lexicon.EndpointRequestTests.

    [Fact]
    public async Task QueryStatusesAsync_Filter_SendsTheQueryStatusesParameters()
    {
        _fixture.On("tools.ozone.moderation.queryStatuses", Json(new { subjectStatuses = Array.Empty<object>() }));

        await Client.Ozone.Moderation.QueryStatusesAsync(
            new SubjectStatusFilter
            {
                ReviewState = SubjectReviewState.Escalated,
                Takendown = true,
                Appealed = false,
                Collections = [Nsid.Parse("app.bsky.feed.post")],
                MinPriorityScore = 5,
            },
            limit: 10);

        // querySubjects never existed; the queue is queryStatuses, with boolean filters.
        var sent = _fixture.To("tools.ozone.moderation.queryStatuses").Single();
        Assert.Equal("/xrpc/tools.ozone.moderation.queryStatuses", sent.Path);
        Assert.Equal(
            "collections=app.bsky.feed.post&reviewState=tools.ozone.moderation.defs#reviewEscalated"
                + "&takendown=true&appealed=false&minPriorityScore=5&limit=10",
            Uri.UnescapeDataString(sent.Query));
    }

    [Fact]
    public async Task QueryStatusesAsync_ReadsSubjectStatuses()
    {
        _fixture.On("tools.ozone.moderation.queryStatuses", Json(new
        {
            cursor = "c1",
            subjectStatuses = new object[]
            {
                new
                {
                    id = 7,
                    subject = new Dictionary<string, object>
                    {
                        ["$type"] = "chat.bsky.convo.defs#convoRef",
                        ["did"] = "did:plc:abc",
                        ["convoId"] = "convo-1",
                    },
                    hosting = new Dictionary<string, object>
                    {
                        ["$type"] = "tools.ozone.moderation.defs#accountHosting",
                        ["status"] = "deactivated",
                    },
                    createdAt = "2026-09-01T00:00:00.000Z",
                    updatedAt = "2026-09-02T00:00:00.000Z",
                    reviewState = SubjectReviewState.Open,
                    takendown = false,
                    priorityScore = 3,
                },
            },
        }));

        var page = await Client.Ozone.Moderation.QueryStatusesAsync();

        var status = Assert.Single(page.SubjectStatuses);
        Assert.Equal("c1", page.Cursor);
        Assert.Equal("convo-1", Assert.IsType<ConvoSubject>(status.Subject).ConvoId);
        Assert.Equal("deactivated", Assert.IsType<AccountHosting>(status.Hosting).Status);
        Assert.Equal(3, status.PriorityScore);
        Assert.False(status.Takendown);
    }

    // Communication (createTemplate, listTemplates), Team (addMember, listMembers), Set
    // (upsertSet, getValues), Server (getConfig) and Signature (findRelatedAccounts) are covered
    // by ATProtoNet.Tests.Lexicon.EndpointRequestTests.
}
