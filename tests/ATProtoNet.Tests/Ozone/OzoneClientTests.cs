using System.Net;
using System.Text.Json;
using ATProtoNet.Lexicon.Tools.Ozone;
using ATProtoNet.Lexicon.Tools.Ozone.Communication;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Server;
using ATProtoNet.Lexicon.Tools.Ozone.Set;
using ATProtoNet.Lexicon.Tools.Ozone.Signature;
using ATProtoNet.Lexicon.Tools.Ozone.Team;

namespace ATProtoNet.Tests.Ozone;

public class OzoneClientTests
{
    private static AtProtoClient CreateClient(FakeHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var options = new AtProtoClientOptions { InstanceUrl = "https://ozone.test" };
        return new AtProtoClient(options, httpClient, null, null);
    }

    private static FakeHandler OkJson(object body) =>
        new(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json")
        });

    // ─── Moderation ───

    [Fact]
    public async Task Ozone_Property_Exists()
    {
        var client = CreateClient(OkJson(new { }));
        Assert.NotNull(client.Ozone);
        Assert.NotNull(client.Ozone.Moderation);
        Assert.NotNull(client.Ozone.Communication);
        Assert.NotNull(client.Ozone.Team);
        Assert.NotNull(client.Ozone.Set);
        Assert.NotNull(client.Ozone.Server);
        Assert.NotNull(client.Ozone.Signature);
    }

    [Fact]
    public async Task EmitEvent_SendsTakedown()
    {
        var response = new
        {
            id = 1L,
            @event = new
            {
                @__type = "tools.ozone.moderation.defs#modEventTakedown",
                comment = "spam",
                durationInHours = 24,
            },
            subject = new
            {
                @__type = "com.atproto.admin.defs#repoRef",
                did = "did:plc:abc",
            },
            createdBy = "did:plc:mod",
            createdAt = "2024-01-01T00:00:00Z",
        };

        // Use raw JSON to get $type as property name
        var json = """
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
        """;

        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var request = new EmitEventRequest
        {
            Event = new ModEventTakedown { Comment = "spam", DurationInHours = 24 },
            Subject = new RepoSubject { Did = "did:plc:abc" },
            CreatedBy = "did:plc:mod",
        };

        var result = await client.Ozone.Moderation.EmitEventAsync(request);

        Assert.Equal(1L, result.Id);
        Assert.Equal("did:plc:mod", result.CreatedBy);
        Assert.Contains("tools.ozone.moderation.emitEvent", handler.LastRequestUri!.ToString());
    }

    [Fact]
    public async Task GetEvent_QueriesById()
    {
        var json = """
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
        """;

        var handler = new FakeHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
        });
        var client = CreateClient(handler);

        var result = await client.Ozone.Moderation.GetEventAsync(42);

        Assert.Equal(42L, result.Id);
        Assert.Contains("id=42", handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task GetRepo_QueriesByDid()
    {
        var response = new
        {
            did = "did:plc:xyz",
            handle = "user.bsky.social",
            indexedAt = "2024-01-01T00:00:00Z",
            moderation = new { },
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Moderation.GetRepoAsync("did:plc:xyz");

        Assert.Equal("did:plc:xyz", result.Did);
        Assert.Contains("did=did", handler.LastRequestUri!.Query);
    }

    [Fact]
    public async Task QueryEvents_PassesParameters()
    {
        var response = new
        {
            events = new object[] { },
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        await client.Ozone.Moderation.QueryEventsAsync(
            subject: "did:plc:abc",
            limit: 10,
            sortDirection: "desc");

        Assert.Contains("subject=did", handler.LastRequestUri!.Query);
        Assert.Contains("limit=10", handler.LastRequestUri.Query);
    }

    [Fact]
    public async Task QuerySubjects_PassesReviewState()
    {
        var response = new { subjects = new object[] { } };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        await client.Ozone.Moderation.QuerySubjectsAsync(
            reviewState: SubjectReviewState.Escalated);

        Assert.Contains("reviewState=", handler.LastRequestUri!.Query);
    }

    // ─── Communication ───

    [Fact]
    public async Task CreateTemplate_ReturnsView()
    {
        var response = new
        {
            id = "tmpl-1",
            name = "Warning",
            contentMarkdown = "You violated...",
            disabled = false,
            lastUpdatedBy = "did:plc:mod",
            createdAt = "2024-01-01T00:00:00Z",
            updatedAt = "2024-01-01T00:00:00Z",
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Communication.CreateTemplateAsync(
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
        var response = new
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
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Communication.ListTemplatesAsync();

        Assert.Single(result.CommunicationTemplates);
    }

    // ─── Team ───

    [Fact]
    public async Task AddMember_ReturnsTeamMember()
    {
        var response = new
        {
            did = "did:plc:newmod",
            role = "tools.ozone.team.defs#roleModerator",
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Team.AddMemberAsync(
            new AddMemberRequest { Did = "did:plc:newmod", Role = TeamMemberRole.Moderator });

        Assert.Equal("did:plc:newmod", result.Did);
        Assert.Equal(TeamMemberRole.Moderator, result.Role);
    }

    [Fact]
    public async Task ListMembers_ReturnsPaginated()
    {
        var response = new
        {
            members = new[]
            {
                new { did = "did:plc:admin", role = "tools.ozone.team.defs#roleAdmin" }
            },
            cursor = "next-page",
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Team.ListMembersAsync(limit: 25);

        Assert.Single(result.Members);
        Assert.Equal("next-page", result.Cursor);
        Assert.Contains("limit=25", handler.LastRequestUri!.Query);
    }

    // ─── Set ───

    [Fact]
    public async Task UpsertSet_CreatesSet()
    {
        var response = new
        {
            name = "bad-words",
            setSize = 0,
            createdAt = "2024-01-01T00:00:00Z",
            updatedAt = "2024-01-01T00:00:00Z",
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Set.UpsertSetAsync(
            new UpsertSetRequest { Name = "bad-words", Description = "Known bad words" });

        Assert.Equal("bad-words", result.Name);
    }

    [Fact]
    public async Task GetValues_ReturnsSetValues()
    {
        var response = new
        {
            set = new
            {
                name = "bad-words",
                setSize = 2,
                createdAt = "2024-01-01T00:00:00Z",
                updatedAt = "2024-01-01T00:00:00Z",
            },
            values = new[] { "word1", "word2" },
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Set.GetValuesAsync("bad-words");

        Assert.Equal(2, result.Values.Count);
        Assert.Contains("name=bad-words", handler.LastRequestUri!.Query);
    }

    // ─── Server ───

    [Fact]
    public async Task GetConfig_ReturnsConfig()
    {
        var response = new
        {
            appview = new { url = "https://api.bsky.app" },
            pds = new { url = "https://pds.example.com" },
            viewer = new { role = "tools.ozone.team.defs#roleAdmin" },
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        var result = await client.Ozone.Server.GetConfigAsync();

        Assert.Equal("https://api.bsky.app", result.Appview?.Url);
        Assert.Equal("tools.ozone.team.defs#roleAdmin", result.Viewer?.Role);
    }

    // ─── Signature ───

    [Fact]
    public async Task FindRelatedAccounts_QueriesByDid()
    {
        var response = new
        {
            accounts = new object[] { },
        };

        var handler = OkJson(response);
        var client = CreateClient(handler);

        await client.Ozone.Signature.FindRelatedAccountsAsync("did:plc:abc");

        Assert.Contains("did=did", handler.LastRequestUri!.Query);
    }

    // ─── Model Serialization ───

    [Fact]
    public void SubjectReviewState_Constants()
    {
        Assert.Equal("tools.ozone.moderation.defs#reviewOpen", SubjectReviewState.Open);
        Assert.Equal("tools.ozone.moderation.defs#reviewEscalated", SubjectReviewState.Escalated);
        Assert.Equal("tools.ozone.moderation.defs#reviewClosed", SubjectReviewState.Closed);
        Assert.Equal("tools.ozone.moderation.defs#reviewNone", SubjectReviewState.None);
    }

    [Fact]
    public void TeamMemberRole_Constants()
    {
        Assert.Equal("tools.ozone.team.defs#roleAdmin", TeamMemberRole.Admin);
        Assert.Equal("tools.ozone.team.defs#roleModerator", TeamMemberRole.Moderator);
        Assert.Equal("tools.ozone.team.defs#roleTriage", TeamMemberRole.Triage);
    }

    [Fact]
    public void ModEventTakedown_Serializes_WithTypeDiscriminator()
    {
        var evt = new ModEventTakedown { Comment = "spam", DurationInHours = 24 };
        var json = JsonSerializer.Serialize<ModEventType>(evt);

        Assert.Contains("\"$type\":\"tools.ozone.moderation.defs#modEventTakedown\"", json);
        Assert.Contains("\"comment\":\"spam\"", json);
        Assert.Contains("\"durationInHours\":24", json);
    }

    [Fact]
    public void ModEventLabel_Serializes_WithLabels()
    {
        var evt = new ModEventLabel
        {
            Comment = "adding label",
            CreateLabelVals = ["spam"],
            NegateLabelVals = ["nsfw"],
        };
        var json = JsonSerializer.Serialize<ModEventType>(evt);

        Assert.Contains("modEventLabel", json);
        Assert.Contains("\"createLabelVals\":[\"spam\"]", json);
    }

    [Fact]
    public void ModerationSubject_RepoRef_Serializes()
    {
        var subject = new RepoSubject { Did = "did:plc:abc" };
        var json = JsonSerializer.Serialize<ModerationSubject>(subject);

        Assert.Contains("\"$type\":\"com.atproto.admin.defs#repoRef\"", json);
        Assert.Contains("\"did\":\"did:plc:abc\"", json);
    }

    [Fact]
    public void ModerationSubject_StrongRef_Serializes()
    {
        var subject = new RecordSubject { Uri = "at://did:plc:abc/app.bsky.feed.post/123", Cid = "bafyabc" };
        var json = JsonSerializer.Serialize<ModerationSubject>(subject);

        Assert.Contains("\"$type\":\"com.atproto.repo.strongRef\"", json);
        Assert.Contains("\"uri\":\"at://did:plc:abc/app.bsky.feed.post/123\"", json);
    }

    // ─── Helper ───

    internal sealed class FakeHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;
        public Uri? LastRequestUri { get; private set; }
        public HttpMethod? LastMethod { get; private set; }

        public FakeHandler(HttpResponseMessage response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            LastMethod = request.Method;
            return Task.FromResult(_response);
        }
    }
}
