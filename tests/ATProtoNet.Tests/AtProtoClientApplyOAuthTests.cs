using System.Net;
using System.Net.Http;
using System.Text;
using ATProtoNet.Auth.OAuth;
using NSubstitute;

namespace ATProtoNet.Tests;

/// <summary>
/// Behavioral guards for <see cref="AtProtoClient.ApplyOAuthSessionAsync"/>'s
/// re-Apply semantics and resource-management invariants documented in CHANGELOG.
/// </summary>
public sealed class AtProtoClientApplyOAuthTests
{
    private static OAuthSessionResult NewSession(string did = "did:plc:test")
    {
        return new OAuthSessionResult
        {
            Did = did,
            Handle = "test.example.com",
            IsHandleVerified = true,
            AccessToken = "access",
            RefreshToken = "refresh",
            TokenType = "DPoP",
            PdsUrl = "https://pds.example.com",
            Issuer = "https://issuer.example.com",
            TokenEndpoint = "https://issuer.example.com/oauth/token",
            DPoP = new DPoPProofGenerator(),
            DpopKeyId = "kid",
            ExpiresIn = 3600,
        };
    }

    private static bool IsDpopDisposed(DPoPProofGenerator dpop)
    {
        try
        {
            dpop.GenerateProof("GET", "https://example.com");
            return false;
        }
        catch (ObjectDisposedException)
        {
            return true;
        }
    }

    [Fact]
    public async Task ApplyOAuthSessionAsync_ReApplyWithNewSession_DisposesPrior()
    {
        using var client = new AtProtoClientBuilder()
            .WithAutoRefreshSession(false)
            .Build();

        var first = NewSession("did:plc:first");
        var second = NewSession("did:plc:second");

        await client.ApplyOAuthSessionAsync(first);
        await client.ApplyOAuthSessionAsync(second);

        Assert.True(IsDpopDisposed(first.DPoP),
            "First session's DPoP key should be disposed when a new session replaces it.");
        Assert.False(IsDpopDisposed(second.DPoP),
            "Currently installed session's DPoP key must remain usable.");
    }

    [Fact]
    public async Task ApplyOAuthSessionAsync_ReApplyWithSameSession_DoesNotDisposeIt()
    {
        using var client = new AtProtoClientBuilder()
            .WithAutoRefreshSession(false)
            .Build();

        var session = NewSession();
        await client.ApplyOAuthSessionAsync(session);
        await client.ApplyOAuthSessionAsync(session); // idempotent re-Apply

        Assert.False(IsDpopDisposed(session.DPoP),
            "Re-applying the same OAuthSessionResult must not dispose its DPoP key.");
    }

    [Fact]
    public async Task RefreshSession_WhenStoreWriteThrows_DoesNotMutateInMemoryTokens()
    {
        // Documented invariant (CHANGELOG): the OAuth refresh path persists
        // rotated tokens to IAtProtoTokenStore BEFORE mutating the in-memory
        // session. If the store throws, the in-memory tokens must remain at
        // their pre-refresh values so the next outbound request fails fast
        // (the auth server has burned the old refresh token, but at least the
        // failure is visible) rather than silently desyncing memory and disk.

        // Backing HTTP handler that returns a valid OAuth token-refresh
        // response, simulating a successful auth-server round-trip.
        var handler = new FakeHttpHandler((req, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                        "access_token": "new-access",
                        "token_type": "DPoP",
                        "refresh_token": "new-refresh",
                        "expires_in": 3600,
                        "scope": "atproto"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
            return Task.FromResult(response);
        });

        using var httpClient = new HttpClient(handler);
        var oauthClient = new OAuthClient(
            new OAuthOptions
            {
                ClientMetadata = new OAuthClientMetadata
                {
                    ClientId = "https://app.example.com/client-metadata.json",
                    RedirectUris = ["https://app.example.com/cb"],
                    GrantTypes = ["authorization_code", "refresh_token"],
                    ResponseTypes = ["code"],
                    Scope = "atproto",
                    TokenEndpointAuthMethod = "none",
                    DpopBoundAccessTokens = true,
                },
                Scope = "atproto",
            },
            httpClient,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        // Token store that throws on StoreAsync.
        var tokenStore = Substitute.For<IAtProtoTokenStore>();
        tokenStore.StoreAsync(Arg.Any<string>(), Arg.Any<AtProtoTokenData>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("simulated store failure")));

        using var client = new AtProtoClientBuilder()
            .WithAutoRefreshSession(false)
            .Build();

        var session = NewSession();
        var originalAccess = session.AccessToken;
        var originalRefresh = session.RefreshToken;
        var originalObtainedAt = session.TokenObtainedAt;

        await client.ApplyOAuthSessionAsync(session, oauthClient, tokenStore);

        // RefreshSessionAsync should throw because StoreAsync threw.
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.RefreshSessionAsync());

        // Persist-before-mutate invariant: in-memory session must still hold
        // the original tokens, not the rotated ones.
        Assert.Same(session, client.OAuthSession);
        Assert.Equal(originalAccess, client.OAuthSession!.AccessToken);
        Assert.Equal(originalRefresh, client.OAuthSession.RefreshToken);
        Assert.Equal(originalObtainedAt, client.OAuthSession.TokenObtainedAt);
    }

    private sealed class FakeHttpHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;

        public FakeHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        {
            _responder = responder;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => _responder(request, cancellationToken);
    }

    [Fact]
    public async Task ApplyOAuthSessionAsync_ReApplyWithoutOAuthClient_PreservesPriorClient()
    {
        // Documented invariant: shorthand re-Apply (`ApplyOAuthSessionAsync(session)`)
        // must NOT silently null out the OAuthClient/IAtProtoTokenStore that an earlier
        // full Apply installed. Verified indirectly: if the prior _oauthClient were
        // nulled, the next RefreshSessionAsync would throw "Cannot refresh OAuth
        // session: no OAuthClient was registered". We trigger the refresh and assert
        // that a DIFFERENT exception surfaces (network/auth failure from the actual
        // refresh attempt), which proves _oauthClient is still set.
        using var client = new AtProtoClientBuilder()
            .WithAutoRefreshSession(false)
            .Build();

        using var httpClient = new HttpClient { BaseAddress = new Uri("https://unreachable.invalid") };
        var oauthClient = new OAuthClient(
            new OAuthOptions
            {
                ClientMetadata = new OAuthClientMetadata
                {
                    ClientId = "https://app.example.com/client-metadata.json",
                    RedirectUris = ["https://app.example.com/cb"],
                    GrantTypes = ["authorization_code", "refresh_token"],
                    ResponseTypes = ["code"],
                    Scope = "atproto",
                    TokenEndpointAuthMethod = "none",
                    DpopBoundAccessTokens = true,
                },
                Scope = "atproto",
            },
            httpClient,
            Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance);

        await client.ApplyOAuthSessionAsync(NewSession(), oauthClient);

        // Re-Apply with positional shorthand (no oauthClient, no tokenStore).
        await client.ApplyOAuthSessionAsync(NewSession("did:plc:second"));

        // RefreshSessionAsync should attempt a real refresh against the (unreachable)
        // endpoint, not bail with the "no OAuthClient" InvalidOperationException.
        var ex = await Record.ExceptionAsync(() => client.RefreshSessionAsync());
        Assert.NotNull(ex);
        Assert.IsNotType<InvalidOperationException>(ex);
    }
}
