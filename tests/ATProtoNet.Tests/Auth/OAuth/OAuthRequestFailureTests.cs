using System.Net;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Tests.Auth;
using Microsoft.Extensions.Logging.Abstractions;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth.OAuth;

/// <summary>
/// How a request to an authorization server fails: within <see cref="OAuthOptions.RequestTimeout"/>
/// even when the server stalls mid-body, and always as an <see cref="OAuthException"/> unless the
/// caller cancelled.
/// </summary>
public sealed class OAuthRequestFailureTests : IDisposable
{
    private readonly StubServer _server = new();
    private readonly HttpClient _http;

    public OAuthRequestFailureTests() => _http = new HttpClient(_server);

    public void Dispose()
    {
        _http.Dispose();
        _server.Dispose();
    }

    private OAuthClient Client(TimeSpan? timeout = null) => new(
        new OAuthOptions
        {
            ClientMetadata = new OAuthClientMetadata { ClientId = ClientId, RedirectUris = [RedirectUri] },
            HttpClient = _http,
            IdentityResolver = AliceIdentity(),
            RequestTimeout = timeout ?? TimeSpan.FromSeconds(30),
        },
        NullLogger.Instance)
    {
        NonceCache = new DPoPNonceCache(),
    };

    private static ATProtoNet.Auth.OAuthSession Revocable() =>
        OAuthSession(NewDPoPKey(), revocationEndpoint: RevocationEndpoint);

    [Fact]
    public async Task ABodyThatStalls_TimesOutAsRequestTimeout()
    {
        // HttpClient.Timeout no longer applies once the headers are in.
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BodyStream(stall: true)) };
        using var oauth = Client(TimeSpan.FromMilliseconds(200));

        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.RevokeAsync(Revocable()));

        Assert.Equal("request_timeout", ex.Error);
    }

    [Fact]
    public async Task TheCallerCancelling_StaysACancellation()
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BodyStream(stall: true)) };
        using var oauth = Client();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oauth.RevokeAsync(Revocable(), cts.Token));
    }

    [Fact]
    public async Task AConnectionFailure_IsRequestFailed()
    {
        _server.Handler = (_, _) => throw new HttpRequestException("connection refused");
        using var oauth = Client();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.RevokeAsync(Revocable()));

        Assert.Equal("request_failed", ex.Error);
    }

    [Fact]
    public async Task ABodyBrokenOffMidRead_IsRequestFailed()
    {
        _server.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BodyStream(stall: false)) };
        using var oauth = Client();

        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.RevokeAsync(Revocable()));

        Assert.Equal("request_failed", ex.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Constructor_ARequestTimeoutThatIsNotPositive_Throws(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Client(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public async Task AnotherErrorCarryingANonce_IsNotRetriedButTheNonceIsKept()
    {
        var cache = new DPoPNonceCache();
        _server.Respond = _ =>
        {
            var error = OAuthError("invalid_request");
            error.Headers.TryAddWithoutValidation("DPoP-Nonce", "n-1");
            return error;
        };
        using var oauth = new OAuthClient(
            new OAuthOptions
            {
                ClientMetadata = new OAuthClientMetadata { ClientId = ClientId, RedirectUris = [RedirectUri] },
                HttpClient = _http,
                IdentityResolver = AliceIdentity(),
            },
            NullLogger.Instance)
        { NonceCache = cache };

        var ex = await Assert.ThrowsAsync<OAuthException>(() => oauth.RevokeAsync(Revocable()));

        Assert.Equal("invalid_request", ex.Error);
        Assert.Single(_server.Requests);
        Assert.Equal("n-1", cache.Get(RevocationEndpoint));
    }

    /// <summary>A response body that never delivers a byte, or breaks off after the first one.</summary>
    private sealed class BodyStream(bool stall) : Stream
    {
        private bool _sent;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (stall)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                return 0;
            }

            if (_sent)
                throw new IOException("The response ended prematurely.");

            _sent = true;
            buffer.Span[0] = (byte)'{';
            return 1;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
