using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;

namespace ATProtoNet.Tests.Lexicon;

/// <summary>
/// Regression tests for the wire bugs the drift test found (#123): each method now sends and
/// reads the shapes its upstream Lexicon defines. Response fixtures are upstream-shaped, several
/// captured from the Bluesky appview.
/// </summary>
public class WireFidelityTests : IDisposable
{
    private const string DidText = "did:plc:z72i7hdynmk6r22z27h6tvur";

    private const string ConvoJson =
        """{"id":"convo-1","rev":"rev-2","members":[],"muted":true,"unreadCount":0,"status":"accepted"}""";

    private const string MessageJson =
        """{"id":"msg-1","rev":"rev-3","text":"hi","sender":{"did":"did:plc:z72i7hdynmk6r22z27h6tvur"},"sentAt":"2026-09-01T00:00:00.000Z"}""";

    private readonly CapturingHandler _handler = new();
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public WireFidelityTests()
    {
        _httpClient = new HttpClient(_handler);
        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com", AutoRefreshSession = false },
            _httpClient, null, null);
    }

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    // ─── Procedures whose Lexicon declares a JSON input ───

    [Fact]
    public async Task DeactivateAccountAsync_NoArguments_SendsAnEmptyJsonBody()
    {
        await _client.Server.DeactivateAccountAsync();

        // The reference PDS rejects a body-less call: "Request encoding (Content-Type) required".
        Assert.Equal("application/json", _handler.ContentType);
        Assert.Equal("{}", _handler.Body);
    }

    [Fact]
    public async Task DeactivateAccountAsync_DeleteAfter_SendsItInTheBody()
    {
        await _client.Server.DeactivateAccountAsync(AtDatetime.Parse("2026-10-01T00:00:00.000Z"));

        Assert.Equal("""{"deleteAfter":"2026-10-01T00:00:00.000Z"}""", _handler.Body);
    }

    [Fact]
    public async Task UpdateAllReadAsync_Status_SendsItAndReadsUpdatedCount()
    {
        _handler.Respond = """{"updatedCount":3}""";

        var result = await _client.Chat.Convo.UpdateAllReadAsync("accepted");

        Assert.Equal("application/json", _handler.ContentType);
        Assert.Equal("""{"status":"accepted"}""", _handler.Body);
        Assert.Equal(3, result.UpdatedCount);
    }

    [Fact]
    public async Task UpdateAllReadAsync_NoStatus_SendsAnEmptyJsonBody()
    {
        _handler.Respond = """{"updatedCount":0}""";

        await _client.Chat.Convo.UpdateAllReadAsync();

        Assert.Equal("application/json", _handler.ContentType);
        Assert.Equal("{}", _handler.Body);
    }

    // ─── Chat outputs ───

    [Theory]
    [InlineData("chat.bsky.convo.muteConvo")]
    [InlineData("chat.bsky.convo.unmuteConvo")]
    [InlineData("chat.bsky.convo.updateRead")]
    public async Task ConvoChanges_ReadTheConvoTheServiceWrapsInAnObject(string nsid)
    {
        _handler.Respond = $$"""{"convo":{{ConvoJson}} }""";

        var convo = nsid switch
        {
            "chat.bsky.convo.muteConvo" => await _client.Chat.Convo.MuteConvoAsync("convo-1"),
            "chat.bsky.convo.unmuteConvo" => await _client.Chat.Convo.UnmuteConvoAsync("convo-1"),
            _ => await _client.Chat.Convo.UpdateReadAsync("convo-1", "msg-1"),
        };

        Assert.Equal($"/xrpc/{nsid}", _handler.Path);
        Assert.Equal("convo-1", convo.Id);
        Assert.Equal("rev-2", convo.Rev);
        Assert.True(convo.Muted);
    }

    [Theory]
    [InlineData("chat.bsky.convo.addReaction")]
    [InlineData("chat.bsky.convo.removeReaction")]
    public async Task ReactionChanges_ReadTheMessageTheServiceWrapsInAnObject(string nsid)
    {
        _handler.Respond = $$"""{"message":{{MessageJson}} }""";

        var message = nsid == "chat.bsky.convo.addReaction"
            ? await _client.Chat.Convo.AddReactionAsync("convo-1", "msg-1", "👍")
            : await _client.Chat.Convo.RemoveReactionAsync("convo-1", "msg-1", "👍");

        Assert.Equal($"/xrpc/{nsid}", _handler.Path);
        Assert.Equal("msg-1", message.Id);
        Assert.Equal("hi", message.Text);
    }

    [Fact]
    public async Task GetConvoAvailabilityAsync_ReadsCanChatAndTheExistingConvo()
    {
        _handler.Respond = $$"""{"canChat":true,"convo":{{ConvoJson}} }""";

        var availability = await _client.Chat.Convo.GetConvoAvailabilityAsync([Did.Parse(DidText)]);

        Assert.True(availability.CanChat);
        Assert.Equal("convo-1", availability.Convo?.Id);
    }

    [Fact]
    public async Task AcceptConvoAsync_ReadsTheRev()
    {
        _handler.Respond = """{"rev":"rev-4"}""";

        var result = await _client.Chat.Convo.AcceptConvoAsync("convo-1");

        Assert.Equal("rev-4", result.Rev);
    }

    [Fact]
    public async Task ExportAccountDataAsync_JsonLines_ReturnsTheBodyUnparsed()
    {
        const string Export = "{\"id\":\"msg-1\"}\n{\"id\":\"msg-2\"}\n";
        _handler.Respond = Export;
        _handler.ResponseContentType = "application/jsonl";

        await using var export = await _client.Chat.Actor.ExportAccountDataAsync();
        using var reader = new StreamReader(export.Content);

        Assert.Equal("/xrpc/chat.bsky.actor.exportAccountData", _handler.Path);
        Assert.Equal("application/jsonl", export.ContentType);
        Assert.Equal(Export, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    // ─── Parameters the Lexicons define ───

    [Fact]
    public async Task SearchPostsAsync_Tags_SendsOneTagParameterEach()
    {
        _handler.Respond = """{"posts":[]}""";

        await _client.Bsky.Feed.SearchPostsAsync("q", tags: ["atproto", "dotnet"]);

        Assert.Equal("?q=q&tag=atproto&tag=dotnet", _handler.Query);
    }

    [Fact]
    public async Task GetListsAsync_Purposes_SendsOnePurposesParameterEach()
    {
        _handler.Respond = """{"lists":[]}""";

        await _client.Bsky.Graph.GetListsAsync(Did.Parse(DidText), purposes: ["modlist", "curatelist"], limit: 5);

        Assert.Equal($"?actor={DidText}&purposes=modlist&purposes=curatelist&limit=5", _handler.Query);
    }

    [Fact]
    public async Task GetFollowersAsync_Sort_SendsItBeforeThePaging()
    {
        _handler.Respond = $$"""{"subject":{"did":"{{DidText}}","handle":"bsky.app"},"followers":[]}""";

        await _client.Bsky.Graph.GetFollowersAsync(Did.Parse(DidText), sort: "top", limit: 5);

        Assert.Equal($"?actor={DidText}&sort=top&limit=5", _handler.Query);
    }

    [Fact]
    public async Task MuteActorAsync_Scope_SendsOnlyRepostsAndOnlyQuoteposts()
    {
        await _client.Bsky.Graph.MuteActorAsync(Did.Parse(DidText), onlyReposts: true, onlyQuoteposts: false);

        Assert.Equal($$"""{"actor":"{{DidText}}","onlyReposts":true,"onlyQuoteposts":false}""", _handler.Body);
    }

    [Fact]
    public async Task UnmuteActorAsync_SendsOnlyTheActor()
    {
        await _client.Bsky.Graph.UnmuteActorAsync(Did.Parse(DidText));

        Assert.Equal($$"""{"actor":"{{DidText}}"}""", _handler.Body);
    }

    [Fact]
    public async Task CreateReportAsync_ModTool_SendsIt()
    {
        _handler.Respond = $$"""{"id":1,"reasonType":"{ {ReportReasons.Spam} }","subject":{"$type":"com.atproto.admin.defs#repoRef","did":"{{DidText}}"},"reportedBy":"{{DidText}}","createdAt":"2026-09-01T00:00:00.000Z"}""";

        await _client.Moderation.CreateReportAsync(
            new RepoSubject { Did = Did.Parse(DidText) },
            ReportReasons.Spam,
            modTool: new ModTool { Name = "bsky-app/android" });

        using var body = JsonDocument.Parse(_handler.Body!);
        Assert.Equal("bsky-app/android", body.RootElement.GetProperty("modTool").GetProperty("name").GetString());
    }

    // ─── Names the Lexicons use ───

    [Fact]
    public async Task GetProfileAsync_ReadsAssociatedAndVerification()
    {
        // Captured from public.api.bsky.app (getProfile, 2026-09-25), trimmed.
        _handler.Respond = $$"""
            {"did":"{{DidText}}","handle":"bsky.app","displayName":"Bluesky",
             "associated":{"lists":18,"feedgens":7,"starterPacks":15,"labeler":false,"chat":{"allowIncoming":"none"},"activitySubscription":{"allowSubscriptions":"followers"} },
             "verification":{"verifications":[{"issuer":"{{DidText}}","issuerDisplayName":"Bluesky","issuerHandle":"bsky.app","uri":"at://{{DidText}}/app.bsky.graph.verification/3lndptszmee2u","isValid":true,"createdAt":"2025-04-21T10:46:44.369Z"}],"verifiedStatus":"valid","trustedVerifierStatus":"valid"},
             "viewer":{"muted":false,"mutedOnlyReposts":true,"blockedBy":false,"mutedByList":{"uri":"at://{{DidText}}/app.bsky.graph.list/3k2la","cid":"bafyreie5737gdxlw5i64vzichcalba3z2v5n6icifvx5xytvske7mr3hpm","name":"Mutes","purpose":"app.bsky.graph.defs#modlist"} } }
            """;

        var profile = await _client.Bsky.Actor.GetProfileAsync(Did.Parse(DidText));

        // associatedChat was never a field: the chat setting lives under associated.
        Assert.Equal("none", profile.Associated?.Chat?.AllowIncoming);
        Assert.Equal(18, profile.Associated?.Lists);
        Assert.Equal("followers", profile.Associated?.ActivitySubscription?.AllowSubscriptions);
        Assert.Equal("valid", profile.Verification?.VerifiedStatus);
        Assert.True(Assert.Single(profile.Verification!.Verifications).IsValid);
        Assert.True(profile.Viewer?.MutedOnlyReposts);
        Assert.Equal(ListPurpose.ModList, profile.Viewer?.MutedByList?.Purpose);
        Assert.Null(profile.ExtensionData);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string Respond { get; set; } = "{}";

        public string ResponseContentType { get; set; } = "application/json";

        public string? Path { get; private set; }

        public string? Query { get; private set; }

        public string? Body { get; private set; }

        public string? ContentType { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Path = request.RequestUri!.AbsolutePath;
            Query = Uri.UnescapeDataString(request.RequestUri.Query);
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            ContentType = request.Content?.Headers.ContentType?.MediaType;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Respond, Encoding.UTF8, ResponseContentType),
            };
        }
    }
}
