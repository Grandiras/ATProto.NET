using System.Net;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Tools.Ozone.Communication;
using ATProtoNet.Lexicon.Tools.Ozone.Moderation;
using ATProtoNet.Lexicon.Tools.Ozone.Set;
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
            Subject = new RepoSubject { Did = Did.Parse("did:plc:abc") },
            CreatedBy = Did.Parse("did:plc:mod"),
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

        var result = await client.Ozone.Moderation.GetRepoAsync(Did.Parse("did:plc:xyz"));

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
    public async Task QueryStatusesAsync_Filter_SendsTheQueryStatusesParameters()
    {
        var handler = OkJson(new { subjectStatuses = new object[] { } });
        var client = CreateClient(handler);

        await client.Ozone.Moderation.QueryStatusesAsync(
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
        Assert.Equal("/xrpc/tools.ozone.moderation.queryStatuses", handler.LastRequestUri!.AbsolutePath);
        Assert.Equal(
            "?collections=app.bsky.feed.post&reviewState=tools.ozone.moderation.defs#reviewEscalated"
                + "&takendown=true&appealed=false&minPriorityScore=5&limit=10",
            Uri.UnescapeDataString(handler.LastRequestUri.Query));
    }

    [Fact]
    public async Task QueryStatusesAsync_ReadsSubjectStatuses()
    {
        var handler = OkJson(new
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
        });
        var client = CreateClient(handler);

        var page = await client.Ozone.Moderation.QueryStatusesAsync();

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
            new AddMemberRequest { Did = Did.Parse("did:plc:newmod"), Role = TeamMemberRole.Moderator });

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

        await client.Ozone.Signature.FindRelatedAccountsAsync(Did.Parse("did:plc:abc"));

        Assert.Contains("did=did", handler.LastRequestUri!.Query);
    }

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
