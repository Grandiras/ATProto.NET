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

    [Fact]
    public async Task GetEvent_QueriesById()
    {
        _fixture.On("tools.ozone.moderation.getEvent", """
            {
                "id": 42,
                "event": {
                    "$type": "tools.ozone.moderation.defs#modEventComment",
                    "comment": "test"
                },
                "subject": {
                    "$type": "com.atproto.admin.defs#repoRef",
                    "did": "did:plc:abc"
                },
                "createdBy": "did:plc:mod",
                "createdAt": "2024-01-01T00:00:00Z"
            }
            """);

        var result = await Client.Ozone.Moderation.GetEventAsync(42);

        Assert.Equal(42L, result.Id);
        Assert.Contains("id=42", _fixture.To("tools.ozone.moderation.getEvent").Single().Query);
    }

    [Fact]
    public async Task GetRepo_QueriesByDid()
    {
        _fixture.On("tools.ozone.moderation.getRepo", Json(new
        {
            did = "did:plc:xyz",
            handle = "user.bsky.social",
            indexedAt = "2024-01-01T00:00:00Z",
            moderation = new { },
        }));

        var result = await Client.Ozone.Moderation.GetRepoAsync(Did.Parse("did:plc:xyz"));

        Assert.Equal("did:plc:xyz", result.Did);
        Assert.Contains("did=did", _fixture.To("tools.ozone.moderation.getRepo").Single().Query);
    }

    [Fact]
    public async Task QueryEvents_PassesParameters()
    {
        _fixture.On("tools.ozone.moderation.queryEvents", Json(new { events = Array.Empty<object>() }));

        await Client.Ozone.Moderation.QueryEventsAsync(
            subject: "did:plc:abc",
            limit: 10,
            sortDirection: "desc");

        var sent = _fixture.To("tools.ozone.moderation.queryEvents").Single();
        Assert.Contains("subject=did", sent.Query);
        Assert.Contains("limit=10", sent.Query);
    }

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

    // ─── Communication ───

    [Fact]
    public async Task CreateTemplate_ReturnsView()
    {
        _fixture.On("tools.ozone.communication.createTemplate", Json(new
        {
            id = "tmpl-1",
            name = "Warning",
            contentMarkdown = "You violated...",
            disabled = false,
            lastUpdatedBy = "did:plc:mod",
            createdAt = "2024-01-01T00:00:00Z",
            updatedAt = "2024-01-01T00:00:00Z",
        }));

        var result = await Client.Ozone.Communication.CreateTemplateAsync(
            new CreateTemplateRequest
            {
                Name = "Warning",
                ContentMarkdown = "You violated...",
                Subject = "Policy Warning",
            });

        Assert.Equal("tmpl-1", result.Id);
        Assert.Equal("Warning", result.Name);
    }

    [Fact]
    public async Task ListTemplates_ReturnsList()
    {
        _fixture.On("tools.ozone.communication.listTemplates", Json(new
        {
            communicationTemplates = new[]
            {
                new
                {
                    id = "t1",
                    name = "Template1",
                    contentMarkdown = "...",
                    disabled = false,
                    lastUpdatedBy = "did:plc:mod",
                    createdAt = "2024-01-01T00:00:00Z",
                    updatedAt = "2024-01-01T00:00:00Z",
                }
            }
        }));

        var result = await Client.Ozone.Communication.ListTemplatesAsync();

        Assert.Single(result.CommunicationTemplates);
    }

    // ─── Team ───

    [Fact]
    public async Task AddMember_ReturnsTeamMember()
    {
        _fixture.On("tools.ozone.team.addMember", Json(new
        {
            did = "did:plc:newmod",
            role = "tools.ozone.team.defs#roleModerator",
        }));

        var result = await Client.Ozone.Team.AddMemberAsync(
            new AddMemberRequest { Did = Did.Parse("did:plc:newmod"), Role = TeamMemberRole.Moderator });

        Assert.Equal("did:plc:newmod", result.Did);
        Assert.Equal(TeamMemberRole.Moderator, result.Role);
    }

    [Fact]
    public async Task ListMembers_ReturnsPaginated()
    {
        _fixture.On("tools.ozone.team.listMembers", Json(new
        {
            members = new[]
            {
                new { did = "did:plc:admin", role = "tools.ozone.team.defs#roleAdmin" }
            },
            cursor = "next-page",
        }));

        var result = await Client.Ozone.Team.ListMembersAsync(limit: 25);

        Assert.Single(result.Members);
        Assert.Equal("next-page", result.Cursor);
        Assert.Contains("limit=25", _fixture.To("tools.ozone.team.listMembers").Single().Query);
    }

    // ─── Set ───

    [Fact]
    public async Task UpsertSet_CreatesSet()
    {
        _fixture.On("tools.ozone.set.upsertSet", Json(new
        {
            name = "bad-words",
            setSize = 0,
            createdAt = "2024-01-01T00:00:00Z",
            updatedAt = "2024-01-01T00:00:00Z",
        }));

        var result = await Client.Ozone.Set.UpsertSetAsync(
            new UpsertSetRequest { Name = "bad-words", Description = "Known bad words" });

        Assert.Equal("bad-words", result.Name);
    }

    [Fact]
    public async Task GetValues_ReturnsSetValues()
    {
        _fixture.On("tools.ozone.set.getValues", Json(new
        {
            set = new
            {
                name = "bad-words",
                setSize = 2,
                createdAt = "2024-01-01T00:00:00Z",
                updatedAt = "2024-01-01T00:00:00Z",
            },
            values = new[] { "word1", "word2" },
        }));

        var result = await Client.Ozone.Set.GetValuesAsync("bad-words");

        Assert.Equal(2, result.Values.Count);
        Assert.Contains("name=bad-words", _fixture.To("tools.ozone.set.getValues").Single().Query);
    }

    // ─── Server ───

    [Fact]
    public async Task GetConfig_ReturnsConfig()
    {
        _fixture.On("tools.ozone.server.getConfig", Json(new
        {
            appview = new { url = "https://api.bsky.app" },
            pds = new { url = "https://pds.example.com" },
            viewer = new { role = "tools.ozone.team.defs#roleAdmin" },
        }));

        var result = await Client.Ozone.Server.GetConfigAsync();

        Assert.Equal("https://api.bsky.app", result.Appview?.Url);
        Assert.Equal("tools.ozone.team.defs#roleAdmin", result.Viewer?.Role);
    }

    // ─── Signature ───

    [Fact]
    public async Task FindRelatedAccounts_QueriesByDid()
    {
        _fixture.On("tools.ozone.signature.findRelatedAccounts", Json(new { accounts = Array.Empty<object>() }));

        await Client.Ozone.Signature.FindRelatedAccountsAsync(Did.Parse("did:plc:abc"));

        Assert.Contains("did=did", _fixture.To("tools.ozone.signature.findRelatedAccounts").Single().Query);
    }
}
