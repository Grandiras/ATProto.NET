using System.Net;
using System.Text;
using System.Threading.Channels;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;
using ATProtoNet.Tap;
using ATProtoNet.Tests.Identity;
using ATProtoNet.Tests.Streaming;

namespace ATProtoNet.Tests.Tap;

/// <summary>
/// The Tap client against the wire shapes indigo's <c>cmd/tap</c> sends and <c>@atproto/tap</c>
/// reads: events, acknowledgements, admin endpoints and Basic auth.
/// </summary>
public sealed class TapClientTests
{
    private const string Did = "did:plc:ewvi7nxzyoun6zhxrhs64oiz";
    private const string RecordCid = "bafyreicuerrgxezkf745depqtasepklz7xoyws7ckhusinymwzuqbxybry";

    // As cmd/tap marshals them: MarshallableEvt{id, type, record|identity}, RecordEvt's fields in
    // declaration order, record and cid omitted when empty.
    private const string CreateEvent =
        $$$"""{"id":12345,"type":"record","record":{"live":true,"did":"{{{Did}}}","rev":"3kb3fge5lm32x","collection":"app.bsky.feed.post","rkey":"3kb3fge5lm32x","action":"create","record":{"$type":"app.bsky.feed.post","createdAt":"2024-10-07T12:00:00.000Z","text":"Hello world!"},"cid":"{{{RecordCid}}}"}}""";

    private const string DeleteEvent =
        $$$"""{"id":12346,"type":"record","record":{"live":false,"did":"{{{Did}}}","rev":"3kb3fge5lm32y","collection":"app.bsky.feed.post","rkey":"3kb3fge5lm32x","action":"delete"}}""";

    private const string IdentityEvent =
        $$$"""{"id":12347,"type":"identity","identity":{"did":"{{{Did}}}","handle":"atproto.com","is_active":true,"status":"active"}}""";

    // ── Events ───────────────────────────────────────────────

    [Fact]
    public void Parse_RecordCreate_ReadsEveryField()
    {
        var evt = Assert.IsType<TapRecordEvent>(TapEvent.Parse(Encoding.UTF8.GetBytes(CreateEvent)));

        Assert.Equal(12345, evt.Id);
        Assert.Equal(ATProtoNet.Identity.Did.Parse(Did), evt.Did);
        Assert.Equal(Tid.Parse("3kb3fge5lm32x"), evt.Rev);
        Assert.Equal(Nsid.Parse("app.bsky.feed.post"), evt.Collection);
        Assert.Equal(RecordKey.Parse("3kb3fge5lm32x"), evt.Rkey);
        Assert.Equal(RepoOpAction.Create, evt.Operation);
        Assert.Equal(Cid.Parse(RecordCid), evt.Cid);
        Assert.True(evt.Live);
        Assert.Equal("Hello world!", evt.Record!.Value.GetProperty("text").GetString());
        Assert.Equal(AtUri.Parse($"at://{Did}/app.bsky.feed.post/3kb3fge5lm32x"), evt.Uri);
    }

    [Fact]
    public void Parse_RecordDelete_HasNoRecordOrCid()
    {
        var evt = Assert.IsType<TapRecordEvent>(TapEvent.Parse(Encoding.UTF8.GetBytes(DeleteEvent)));

        Assert.Equal(RepoOpAction.Delete, evt.Operation);
        Assert.Null(evt.Record);
        Assert.Null(evt.Cid);
        Assert.False(evt.Live);
        Assert.Null(evt.GetRecord<object>());
    }

    [Fact]
    public void Parse_Identity_ReadsEveryField()
    {
        var evt = Assert.IsType<TapIdentityEvent>(TapEvent.Parse(Encoding.UTF8.GetBytes(IdentityEvent)));

        Assert.Equal(12347, evt.Id);
        Assert.Equal(ATProtoNet.Identity.Did.Parse(Did), evt.Did);
        Assert.Equal(Handle.Parse("atproto.com"), evt.Handle);
        Assert.True(evt.IsActive);
        Assert.Equal(TapRepoStatus.Active, evt.Status);
    }

    [Fact]
    public void Parse_RecordEvent_IsAnIRecordEvent()
    {
        IRecordEvent evt = (TapRecordEvent)TapEvent.Parse(Encoding.UTF8.GetBytes(CreateEvent));

        Assert.Equal(Tid.Parse("3kb3fge5lm32x"), evt.Rev);
        Assert.Equal("Hello world!", evt.GetRecord<ATProtoNet.Lexicon.App.Bsky.Feed.PostRecord>()!.Text);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"id":1,"type":"commit","commit":{}}""")]
    [InlineData("""{"id":1,"type":"identity","identity":{"did":"not-a-did","handle":"","is_active":true,"status":"active"}}""")]
    [InlineData("""{"id":1,"type":"record","record":{"live":true,"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","rev":"3kb3fge5lm32x","collection":"app.bsky.feed.post","rkey":"3kb3fge5lm32x","action":"rename"}}""")]
    [InlineData("""{"type":"identity","identity":{"did":"did:plc:ewvi7nxzyoun6zhxrhs64oiz","handle":"","is_active":true,"status":"active"}}""")]
    public void Parse_Invalid_ThrowsFormatException(string json)
    {
        Assert.Throws<FormatException>(() => TapEvent.Parse(Encoding.UTF8.GetBytes(json)));
    }

    // ── Admin endpoints ──────────────────────────────────────

    [Fact]
    public void FormatAdminAuthHeader_MatchesTheReferenceClient()
    {
        // @atproto/tap README: formatAdminAuthHeader('secret') => 'Basic YWRtaW46c2VjcmV0'
        Assert.Equal("Basic YWRtaW46c2VjcmV0", TapClient.FormatAdminAuthHeader("secret"));
    }

    [Fact]
    public async Task AddReposAsync_PostsTheDidsWithAdminAuth()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var tap = Client(handler, "secret");

        await tap.AddReposAsync([ATProtoNet.Identity.Did.Parse(Did), ATProtoNet.Identity.Did.Parse("did:web:example.com")]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("POST", request.Method);
        Assert.Equal("http://localhost:2480/repos/add", request.Uri);
        Assert.Equal("Basic YWRtaW46c2VjcmV0", request.Authorization);
        Assert.Equal("application/json", request.ContentType);
        Assert.Equal($$"""{"dids":["{{Did}}","did:web:example.com"]}""", request.Body);
    }

    [Fact]
    public async Task RemoveReposAsync_PostsToRemove()
    {
        var handler = new CapturingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        using var tap = Client(handler);

        await tap.RemoveReposAsync([ATProtoNet.Identity.Did.Parse(Did)]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal("http://localhost:2480/repos/remove", request.Uri);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task AddReposAsync_Refused_ThrowsWithTheStatus()
    {
        using var tap = Client(new CapturingHandler(_ => ScriptedHandler.Json("""{"error":"Unauthorized"}""", HttpStatusCode.Unauthorized)));

        var ex = await Assert.ThrowsAsync<TapException>(() => tap.AddReposAsync([ATProtoNet.Identity.Did.Parse(Did)]));

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
    }

    [Fact]
    public async Task ResolveDidAsync_ReadsTheDocument()
    {
        var handler = new CapturingHandler(_ => ScriptedHandler.Json(DidDocs.AtprotoDotCom));
        using var tap = Client(handler);

        var document = await tap.ResolveDidAsync(ATProtoNet.Identity.Did.Parse(Did));

        Assert.Equal($"http://localhost:2480/resolve/{Did}", Assert.Single(handler.Requests).Uri);
        Assert.Equal(ATProtoNet.Identity.Did.Parse(Did), document!.Id);
        Assert.Equal(new Uri("https://enoki.us-east.host.bsky.network"), document.GetPdsEndpoint());
    }

    [Fact]
    public async Task ResolveDidAsync_NotFound_ReturnsNull()
    {
        using var tap = Client(new CapturingHandler(_ => ScriptedHandler.Json("""{"message":"DID not found"}""", HttpStatusCode.NotFound)));

        Assert.Null(await tap.ResolveDidAsync(ATProtoNet.Identity.Did.Parse(Did)));
    }

    [Fact]
    public async Task GetRepoInfoAsync_ActiveRepository()
    {
        // As cmd/tap's handleInfoRepo writes it (Go sorts map keys).
        var handler = new CapturingHandler(_ => ScriptedHandler.Json(
            $$"""{"did":"{{Did}}","error":"","handle":"atproto.com","records":1234,"retries":0,"rev":"3mwgncs2p2324","state":"active"}"""));
        using var tap = Client(handler);

        var info = await tap.GetRepoInfoAsync(ATProtoNet.Identity.Did.Parse(Did));

        Assert.Equal($"http://localhost:2480/info/{Did}", Assert.Single(handler.Requests).Uri);
        Assert.Equal(Handle.Parse("atproto.com"), info.Handle);
        Assert.Equal("active", info.State);
        Assert.Equal(Tid.Parse("3mwgncs2p2324"), info.Rev);
        Assert.Equal(1234, info.Records);
        Assert.Null(info.Error);
        Assert.Equal(0, info.Retries);
    }

    [Fact]
    public async Task GetRepoInfoAsync_PendingRepository_HasNoRevisionOrHandle()
    {
        using var tap = Client(new CapturingHandler(_ => ScriptedHandler.Json(
            $$"""{"did":"{{Did}}","error":"failed to resolve DID","handle":"","records":0,"retries":3,"rev":"","state":"error"}""")));

        var info = await tap.GetRepoInfoAsync(ATProtoNet.Identity.Did.Parse(Did));

        Assert.Null(info.Handle);
        Assert.Null(info.Rev);
        Assert.Equal("failed to resolve DID", info.Error);
        Assert.Equal(3, info.Retries);
    }

    [Fact]
    public async Task GetRepoInfoAsync_Untracked_ThrowsNotFound()
    {
        using var tap = Client(new CapturingHandler(_ => ScriptedHandler.Json("""{"message":"repo not found"}""", HttpStatusCode.NotFound)));

        var ex = await Assert.ThrowsAsync<TapException>(() => tap.GetRepoInfoAsync(ATProtoNet.Identity.Did.Parse(Did)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Theory]
    [InlineData("ftp://localhost:2480")]
    [InlineData("ws://localhost:2480")]
    public void Constructor_NotHttp_Throws(string url)
    {
        Assert.Throws<ArgumentException>(() => new TapClient(new Uri(url)));
    }

    // ── The channel ──────────────────────────────────────────

    [Theory]
    [InlineData("http://localhost:2480", "ws://localhost:2480/channel")]
    [InlineData("https://tap.example.com", "wss://tap.example.com/channel")]
    public void OpenChannel_UsesTheChannelWebSocket(string url, string expected)
    {
        using var tap = new TapClient(new Uri(url));

        Assert.Equal(new Uri(expected), tap.OpenChannel().Endpoint);
    }

    [Fact]
    public async Task ReadAllAsync_DeliversEventsAndSendsAcks()
    {
        var socket = new FakeSocket(CreateEvent, IdentityEvent);
        var connector = new FakeConnector(socket);
        using var tap = Client(new CapturingHandler(_ => new HttpResponseMessage()), "secret", connector);
        var channel = tap.OpenChannel();

        var seen = new List<TapEvent>();
        await foreach (var evt in channel.ReadAllAsync())
        {
            seen.Add(evt);
            await channel.AckAsync(evt);
            if (seen.Count == 2)
                break;
        }

        Assert.Equal([12345L, 12347L], seen.Select(e => e.Id));
        Assert.Equal(["""{"type":"ack","id":12345}""", """{"type":"ack","id":12347}"""], socket.Sent);
        Assert.Equal("Basic YWRtaW46c2VjcmV0", Assert.Single(connector.Options).Authorization);
        Assert.Equal(new Uri("ws://localhost:2480/channel"), Assert.Single(connector.Endpoints));
    }

    [Fact]
    public async Task AckAsync_WithoutAConnection_IsSentOnceOneOpens()
    {
        var socket = new FakeSocket(CreateEvent);
        using var tap = Client(new CapturingHandler(_ => new HttpResponseMessage()), connector: new FakeConnector(socket));
        var channel = tap.OpenChannel();

        await channel.AckAsync(7);
        Assert.Equal(1, channel.PendingAcks);

        await foreach (var _ in channel.ReadAllAsync())
            break;

        Assert.Equal(["""{"type":"ack","id":7}"""], socket.Sent);
        Assert.Equal(0, channel.PendingAcks);
    }

    [Fact]
    public async Task ReadAllAsync_ConnectionDrops_ReconnectsAndFlushesTheAcksItMissed()
    {
        var first = new FakeSocket(CreateEvent) { FailAfterMessages = true };
        var second = new FakeSocket(IdentityEvent);
        var connector = new FakeConnector(first, second);
        using var tap = Client(new CapturingHandler(_ => new HttpResponseMessage()), connector: connector);
        var channel = tap.OpenChannel();

        var seen = new List<TapEvent>();
        await foreach (var evt in channel.ReadAllAsync())
        {
            seen.Add(evt);
            if (evt.Id == 12345)
            {
                // The ack races the dropped connection: it waits for the next one.
                first.Broken = true;
                await channel.AckAsync(evt);
            }
            else
            {
                break;
            }
        }

        Assert.Equal([12345L, 12347L], seen.Select(e => e.Id));
        Assert.Equal(2, connector.Endpoints.Count);
        Assert.Equal(["""{"type":"ack","id":12345}"""], second.Sent);
    }

    [Fact]
    public async Task ReadAllAsync_InvalidMessage_IsReportedAndSkipped()
    {
        var errors = new List<Exception>();
        var socket = new FakeSocket("""{"id":1,"type":"commit"}""", CreateEvent);
        using var tap = new TapClient(
            new TapClientOptions { ServiceUrl = new Uri("http://localhost:2480"), OnError = errors.Add },
            new FakeConnector(socket).Connect);
        var channel = tap.OpenChannel();

        await foreach (var evt in channel.ReadAllAsync())
        {
            Assert.Equal(12345, evt.Id);
            break;
        }

        Assert.IsType<FormatException>(Assert.Single(errors));
        Assert.Empty(socket.Sent);
    }

    [Fact]
    public async Task ReadAllAsync_RefusedUpgrade_Throws()
    {
        var connector = new FakeConnector(new EventStreamException("websocket not available in webhook mode", statusCode: 400));
        using var tap = Client(new CapturingHandler(_ => new HttpResponseMessage()), connector: connector);

        var ex = await Assert.ThrowsAsync<EventStreamException>(async () =>
        {
            await foreach (var _ in tap.OpenChannel().ReadAllAsync())
            {
            }
        });

        Assert.Equal(400, ex.StatusCode);
    }

    [Fact]
    public async Task ReadAllAsync_Cancelled_EndsNormally()
    {
        using var cts = new CancellationTokenSource();
        var socket = new FakeSocket(CreateEvent) { StayOpen = true };
        using var tap = Client(new CapturingHandler(_ => new HttpResponseMessage()), connector: new FakeConnector(socket));

        var seen = 0;
        await foreach (var _ in tap.OpenChannel().ReadAllAsync(cts.Token))
        {
            seen++;
            await cts.CancelAsync();
        }

        Assert.Equal(1, seen);
    }

    private static TapClient Client(HttpMessageHandler handler, string? password = null, FakeConnector? connector = null) => new(
        new TapClientOptions
        {
            ServiceUrl = new Uri("http://localhost:2480"),
            AdminPassword = password,
            HttpClient = new HttpClient(handler),
            Reconnect = StreamTestExtensions.Immediate(3),
        },
        (connector ?? new FakeConnector()).Connect);

    /// <summary>Records each request with its body.</summary>
    private sealed class CapturingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<(string Method, string Uri, string? Authorization, string? ContentType, string? Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add((
                request.Method.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.TryGetValues("Authorization", out var auth) ? auth.Single() : null,
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return respond(request);
        }
    }

    /// <summary>Hands out scripted sockets, or a scripted failure, one per connection.</summary>
    private sealed class FakeConnector(params object[] connections)
    {
        private int _next;

        public List<Uri> Endpoints { get; } = [];

        public List<StreamSocketOptions> Options { get; } = [];

        public ValueTask<IDuplexStreamSocket> Connect(Uri endpoint, StreamSocketOptions options, CancellationToken cancellationToken)
        {
            Endpoints.Add(endpoint);
            Options.Add(options);
            return _next < connections.Length && connections[_next++] is var connection
                ? connection switch
                {
                    IDuplexStreamSocket socket => ValueTask.FromResult(socket),
                    Exception ex => ValueTask.FromException<IDuplexStreamSocket>(ex),
                    _ => throw new InvalidOperationException(),
                }
                : ValueTask.FromException<IDuplexStreamSocket>(new EventStreamException("No more connections.", statusCode: 404));
        }
    }

    /// <summary>A socket that serves its messages, then closes, fails, or waits.</summary>
    private sealed class FakeSocket(params string[] messages) : IDuplexStreamSocket
    {
        private readonly Channel<string> _incoming = CreateChannel(messages);

        public List<string> Sent { get; } = [];

        /// <summary>Throw, as a dropped connection does, once the messages run out.</summary>
        public bool FailAfterMessages { get; init; }

        /// <summary>Wait for cancellation once the messages run out, as an idle connection does.</summary>
        public bool StayOpen { get; init; }

        /// <summary>Sends fail, as they do on a connection that dropped.</summary>
        public bool Broken { get; set; }

        private static Channel<string> CreateChannel(string[] messages)
        {
            var channel = Channel.CreateUnbounded<string>();
            foreach (var message in messages)
                channel.Writer.TryWrite(message);
            channel.Writer.Complete();
            return channel;
        }

        public async ValueTask<StreamSocketMessage?> ReceiveAsync(CancellationToken cancellationToken)
        {
            if (_incoming.Reader.TryRead(out var message))
                return new StreamSocketMessage(Encoding.UTF8.GetBytes(message), IsBinary: false);

            if (StayOpen)
                await Task.Delay(Timeout.Infinite, cancellationToken);
            if (FailAfterMessages)
                throw new System.Net.WebSockets.WebSocketException("The remote party closed the connection without completing the close handshake.");
            return null;
        }

        public ValueTask SendTextAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken)
        {
            if (Broken)
                throw new System.Net.WebSockets.WebSocketException("The connection is closed.");
            Sent.Add(Encoding.UTF8.GetString(utf8.Span));
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
