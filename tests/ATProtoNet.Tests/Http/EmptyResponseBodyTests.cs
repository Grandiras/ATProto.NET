using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Lexicon.App.Bsky.Notification;
using ATProtoNet.Lexicon.Com.AtProto.Identity;

namespace ATProtoNet.Tests.Http;

/// <summary>
/// Every XRPC procedure whose Lexicon declares no output: the server answers with 200
/// and an empty body.
/// </summary>
/// <remarks>
/// Asking for a deserialized response from these throws
/// <c>JsonException: The input does not contain any JSON tokens</c> before the caller
/// ever sees a result, so they fail against a real server no matter what it does. The
/// gap went unnoticed because the test doubles all returned <c>{}</c>, which
/// deserializes perfectly well — this fixture returns what a real PDS returns.
/// </remarks>
public class EmptyResponseBodyTests : IDisposable
{
    private readonly EmptyBodyHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly AtProtoClient _client;

    public EmptyResponseBodyTests()
    {
        _handler = new EmptyBodyHandler();
        _httpClient = new HttpClient(_handler)
        {
            BaseAddress = new Uri("https://pds.example.com/"),
        };

        _client = new AtProtoClient(
            new AtProtoClientOptions { InstanceUrl = "https://pds.example.com" },
            _httpClient,
            null,
            null);
    }

    private static readonly string[] VoidProcedures =
    [
        "com.atproto.identity.updateHandle",
        "com.atproto.identity.requestPlcOperationSignature",
        "com.atproto.identity.submitPlcOperation",
        "com.atproto.sync.notifyOfUpdate",
        "com.atproto.sync.requestCrawl",
        "app.bsky.actor.putPreferences",
        "app.bsky.graph.muteActor",
        "app.bsky.graph.unmuteActor",
        "app.bsky.graph.muteActorList",
        "app.bsky.graph.unmuteActorList",
        "app.bsky.graph.muteThread",
        "app.bsky.graph.unmuteThread",
        "app.bsky.notification.updateSeen",
        "app.bsky.notification.registerPush",
        "tools.ozone.communication.deleteTemplate",
        "tools.ozone.team.deleteMember",
        "tools.ozone.set.deleteSet",
        "tools.ozone.set.addValues",
        "tools.ozone.set.deleteValues",
    ];

    public static TheoryData<string> VoidProcedureNames()
    {
        var data = new TheoryData<string>();

        foreach (var nsid in VoidProcedures)
        {
            data.Add(nsid);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(VoidProcedureNames))]
    public async Task VoidProcedure_ToleratesAnEmptyResponseBody(string nsid)
    {
        await Invoke(nsid);

        var request = Assert.Single(_handler.Requests);
        Assert.Contains(nsid, request);
    }

    [Fact]
    public async Task EveryListedProcedureIsDispatchable()
    {
        // The list is maintained by hand: nothing here can notice a *new* void procedure
        // being added to a Lexicon client, so adding one means adding a case below. What
        // this does catch is the list and the switch drifting apart — a name with no
        // invocation, or a duplicate that quietly tests one endpoint twice.
        Assert.Equal(VoidProcedures.Length, VoidProcedures.Distinct().Count());

        foreach (var name in VoidProcedures)
        {
            var ex = await Record.ExceptionAsync(() => Invoke(name));
            Assert.IsNotType<ArgumentOutOfRangeException>(ex);
        }
    }

    private Task Invoke(string nsid) => nsid switch
    {
        "com.atproto.identity.updateHandle" =>
            _client.Identity.UpdateHandleAsync("alice.example.com"),
        "com.atproto.identity.requestPlcOperationSignature" =>
            _client.Identity.RequestPlcOperationSignatureAsync(),
        "com.atproto.identity.submitPlcOperation" =>
            _client.Identity.SubmitPlcOperationAsync(
                new SubmitPlcOperationRequest { Operation = new Dictionary<string, object>() }),
        "com.atproto.sync.notifyOfUpdate" =>
            _client.Sync.NotifyOfUpdateAsync("pds.example.com"),
        "com.atproto.sync.requestCrawl" =>
            _client.Sync.RequestCrawlAsync("pds.example.com"),
        "app.bsky.actor.putPreferences" =>
            _client.Bsky.Actor.PutPreferencesAsync([]),
        "app.bsky.graph.muteActor" =>
            _client.Bsky.Graph.MuteActorAsync("did:plc:alice"),
        "app.bsky.graph.unmuteActor" =>
            _client.Bsky.Graph.UnmuteActorAsync("did:plc:alice"),
        "app.bsky.graph.muteActorList" =>
            _client.Bsky.Graph.MuteActorListAsync("at://did:plc:alice/app.bsky.graph.list/1"),
        "app.bsky.graph.unmuteActorList" =>
            _client.Bsky.Graph.UnmuteActorListAsync("at://did:plc:alice/app.bsky.graph.list/1"),
        "app.bsky.graph.muteThread" =>
            _client.Bsky.Graph.MuteThreadAsync("at://did:plc:alice/app.bsky.feed.post/1"),
        "app.bsky.graph.unmuteThread" =>
            _client.Bsky.Graph.UnmuteThreadAsync("at://did:plc:alice/app.bsky.feed.post/1"),
        "app.bsky.notification.updateSeen" =>
            _client.Bsky.Notification.UpdateSeenAsync("2026-07-25T00:00:00.000Z"),
        "app.bsky.notification.registerPush" =>
            _client.Bsky.Notification.RegisterPushAsync(new RegisterPushRequest
            {
                ServiceDid = "did:web:push.example.com",
                Token = "token",
                Platform = "web",
                AppId = "com.example.app",
            }),
        "tools.ozone.communication.deleteTemplate" =>
            _client.Ozone.Communication.DeleteTemplateAsync("template-1"),
        "tools.ozone.team.deleteMember" =>
            _client.Ozone.Team.DeleteMemberAsync("did:plc:alice"),
        "tools.ozone.set.deleteSet" =>
            _client.Ozone.Set.DeleteSetAsync("set-1"),
        "tools.ozone.set.addValues" =>
            _client.Ozone.Set.AddValuesAsync("set-1", ["a"]),
        "tools.ozone.set.deleteValues" =>
            _client.Ozone.Set.DeleteValuesAsync("set-1", ["a"]),
        _ => throw new ArgumentOutOfRangeException(nameof(nsid), nsid, "Unmapped procedure."),
    };

    public void Dispose()
    {
        _client.Dispose();
        _httpClient.Dispose();
        _handler.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Answers every request the way a real PDS answers a procedure with no output:
    /// 200, and nothing in the body.
    /// </summary>
    private sealed class EmptyBodyHandler : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.PathAndQuery);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("", Encoding.UTF8, "application/json"),
            });
        }
    }
}
