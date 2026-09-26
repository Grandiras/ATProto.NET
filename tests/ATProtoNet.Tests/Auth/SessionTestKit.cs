using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using ATProtoNet.Tests.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace ATProtoNet.Tests.Auth;

/// <summary>One request as the stub server saw it.</summary>
internal sealed record StubRequest(
    string Method, Uri Uri, string? Authorization, string? DPoP, IReadOnlyDictionary<string, string> Headers, string? Body)
{
    public string Path => Uri.AbsolutePath;

    /// <summary>The XRPC method, for a request to <c>/xrpc/{nsid}</c>.</summary>
    public string? Nsid => Path.StartsWith("/xrpc/", StringComparison.Ordinal) ? Path["/xrpc/".Length..] : null;

    /// <summary>The form fields of a <c>application/x-www-form-urlencoded</c> body.</summary>
    public IReadOnlyDictionary<string, string> Form =>
        (Body ?? "").Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1].Replace('+', ' ')));

    /// <summary>The claims of the request's DPoP proof.</summary>
    public JsonElement DPoPClaims =>
        Jwt.TryDecode(DPoP ?? throw new InvalidOperationException("The request carries no DPoP proof."), out var proof, out var error)
            ? proof.Payload
            : throw new InvalidOperationException(error);
}

/// <summary>
/// A PDS, authorization server and DID directory in one handler: each test scripts the answers,
/// and every request is recorded.
/// </summary>
/// <remarks>
/// The OAuth metadata documents (<c>/.well-known/oauth-protected-resource</c> and
/// <c>/.well-known/oauth-authorization-server</c>) are answered by the stub itself, as a PDS at
/// the requested origin and the authorization server <see cref="SessionKit.Issuer"/>, and kept
/// apart in <see cref="MetadataRequests"/>, so the scripted answers and the recorded requests are
/// only the ones a test is about. Set <see cref="ServesOAuthMetadata"/> to script them too.
/// </remarks>
internal sealed class StubServer : HttpMessageHandler
{
    private readonly ConcurrentQueue<StubRequest> _requests = new();
    private readonly ConcurrentQueue<Uri> _metadataRequests = new();

    /// <summary>Whether the stub answers the OAuth metadata requests itself. Default: true.</summary>
    public bool ServesOAuthMetadata { get; set; } = true;

    /// <summary>The OAuth metadata requests the stub answered itself.</summary>
    public IReadOnlyList<Uri> MetadataRequests => [.. _metadataRequests];

    /// <summary>Answers a request; the token is the one the client passed in.</summary>
    public Func<StubRequest, CancellationToken, Task<HttpResponseMessage>> Handler { get; set; } =
        (request, _) => throw new InvalidOperationException($"Unexpected request to {request.Uri}");

    /// <summary>Sets a synchronous handler.</summary>
    public Func<StubRequest, HttpResponseMessage> Respond
    {
        set => Handler = (request, _) => Task.FromResult(value(request));
    }

    public IReadOnlyList<StubRequest> Requests => [.. _requests];

    public IReadOnlyList<StubRequest> To(string nsidOrPath) =>
        [.. _requests.Where(r => r.Nsid == nsidOrPath || r.Path == nsidOrPath)];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ServesOAuthMetadata && SessionKit.OAuthMetadata(request.RequestUri!) is { } metadata)
        {
            _metadataRequests.Enqueue(request.RequestUri!);
            return metadata;
        }

        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value), StringComparer.OrdinalIgnoreCase);
        var recorded = new StubRequest(
            request.Method.Method,
            request.RequestUri!,
            request.Headers.Authorization?.ToString(),
            headers.GetValueOrDefault("DPoP"),
            headers,
            body);

        _requests.Enqueue(recorded);
        return await Handler(recorded, cancellationToken);
    }
}

/// <summary>Responses, tokens and sessions for the session tests.</summary>
internal static class SessionKit
{
    public static readonly Did Alice = Did.Parse("did:plc:alice");
    public static readonly Handle AliceHandle = Handle.Parse("alice.test");

    public const string Issuer = "https://auth.example.com";
    public static readonly Uri Pds = new("https://pds.example.com/");
    public static readonly Uri TokenEndpoint = new("https://auth.example.com/oauth/token");
    public static readonly Uri RevocationEndpoint = new("https://auth.example.com/oauth/revoke");
    public const string ClientId = "https://app.example.com/client-metadata.json";

    public static HttpResponseMessage JsonResponse(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    public static HttpResponseMessage XrpcError(string error, HttpStatusCode status = HttpStatusCode.BadRequest) =>
        JsonResponse($$"""{"error":"{{error}}","message":"{{error}}"}""", status);

    public static HttpResponseMessage OAuthError(string error, HttpStatusCode status = HttpStatusCode.BadRequest) =>
        JsonResponse($$"""{"error":"{{error}}","error_description":"{{error}} described"}""", status);

    /// <summary>
    /// Authorization server metadata as the AT Protocol profile requires it: the issuer, its
    /// endpoints under <paramref name="issuer"/>, and the required capabilities.
    /// </summary>
    public static string AuthorizationServerMetadataJson(string issuer = Issuer, bool privateKeyJwt = true) => $$"""
        {
          "issuer": "{{issuer}}",
          "authorization_endpoint": "{{issuer}}/oauth/authorize",
          "token_endpoint": "{{issuer}}/oauth/token",
          "pushed_authorization_request_endpoint": "{{issuer}}/oauth/par",
          "revocation_endpoint": "{{issuer}}/oauth/revoke",
          "require_pushed_authorization_requests": true,
          "authorization_response_iss_parameter_supported": true,
          "client_id_metadata_document_supported": true,
          "scopes_supported": ["atproto", "transition:generic"],
          "dpop_signing_alg_values_supported": ["ES256"],
          "token_endpoint_auth_methods_supported": ["none"{{(privateKeyJwt ? ", \"private_key_jwt\"" : "")}}],
          "token_endpoint_auth_signing_alg_values_supported": ["ES256"]
        }
        """;

    /// <summary>
    /// The answer to an OAuth metadata request: protected-resource metadata naming the requested
    /// origin as the resource and <see cref="Issuer"/> as its authorization server, or
    /// <see cref="AuthorizationServerMetadataJson"/> for the requested origin. <see langword="null"/>
    /// for any other request.
    /// </summary>
    public static HttpResponseMessage? OAuthMetadata(Uri url)
    {
        var origin = url.GetLeftPart(UriPartial.Authority);
        return url.AbsolutePath switch
        {
            "/.well-known/oauth-protected-resource" =>
                JsonResponse($$"""{"resource":"{{origin}}","authorization_servers":["{{Issuer}}"]}"""),
            "/.well-known/oauth-authorization-server" => JsonResponse(AuthorizationServerMetadataJson(origin)),
            _ => null,
        };
    }

    /// <summary>
    /// An identity resolver that knows only Alice: <see cref="AliceHandle"/>, verified, hosted on
    /// <paramref name="pds"/> (default <see cref="Pds"/>).
    /// </summary>
    public static IIdentityResolver AliceIdentity(Uri? pds = null)
    {
        pds ??= Pds;
        var identity = new ResolvedIdentity(
            Alice, AliceHandle, HandleVerified: true, pds, DidDocs.Parse(Alice.Value, AliceHandle.Value, pds.OriginalString));

        var resolver = Substitute.For<IIdentityResolver>();
        resolver.ResolveAsync(Arg.Any<AtIdentifier>(), Arg.Any<CancellationToken>()).Returns(identity);
        resolver.ResolveUncachedAsync(Arg.Any<Did>(), Arg.Any<CancellationToken>()).Returns(identity);
        return resolver;
    }

    /// <summary>
    /// A PDS-style access JWT: unsigned for the client's purposes, which only reads its
    /// <c>exp</c>. <paramref name="id"/> tells tokens apart.
    /// </summary>
    public static string AccessJwt(string id, DateTimeOffset? expiresAt = null)
    {
        var exp = (expiresAt ?? DateTimeOffset.UtcNow.AddHours(2)).ToUnixTimeSeconds();
        return TestJws.Mint(
            new { typ = "at+jwt", alg = "HS256" },
            new { scope = "com.atproto.access", sub = Alice.Value, exp, jti = id },
            _ => new byte[32]);
    }

    /// <summary>A <c>createSession</c> / <c>refreshSession</c> / <c>createAccount</c> response.</summary>
    public static HttpResponseMessage SessionResponse(
        string accessJwt, string refreshJwt, string handle = "alice.test", string? pdsInDidDoc = null, string? didDocId = null)
    {
        var didDoc = pdsInDidDoc is null ? "" : $$"""
            ,"didDoc":{"id":"{{didDocId ?? Alice.Value}}","alsoKnownAs":["at://{{handle}}"],
              "service":[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"{{pdsInDidDoc}}"}]}
            """;
        return JsonResponse($$"""{"did":"{{Alice.Value}}","handle":"{{handle}}","accessJwt":"{{accessJwt}}","refreshJwt":"{{refreshJwt}}"{{didDoc}}}""");
    }

    public static HttpResponseMessage GetSessionResponse(string handle = "alice.test", string? email = null) =>
        JsonResponse($$"""{"did":"{{Alice.Value}}","handle":"{{handle}}"{{(email is null ? "" : $",\"email\":\"{email}\"")}}}""");

    public static HttpResponseMessage TokenResponse(string accessToken, string? refreshToken, int expiresIn = 900) =>
        JsonResponse($$"""
            {"access_token":"{{accessToken}}","token_type":"DPoP",{{(refreshToken is null ? "" : $"\"refresh_token\":\"{refreshToken}\",")}}
             "expires_in":{{expiresIn}},"scope":"atproto transition:generic","sub":"{{Alice.Value}}"}
            """);

    /// <summary>A token response for <paramref name="sub"/> with <paramref name="scope"/>; either omitted when null.</summary>
    public static HttpResponseMessage TokenResponseFor(string? sub, string? scope = "atproto transition:generic") =>
        JsonResponse(
            "{\"access_token\":\"at-2\",\"token_type\":\"DPoP\",\"refresh_token\":\"rt-2\",\"expires_in\":900" +
            (scope is null ? "" : $",\"scope\":\"{scope}\"") +
            (sub is null ? "" : $",\"sub\":\"{sub}\"") + "}");

    /// <summary>The pushed authorization endpoint's answer.</summary>
    public static HttpResponseMessage ParResponse() =>
        JsonResponse("""{"request_uri":"urn:ietf:params:oauth:request_uri:stub","expires_in":60}""");

    /// <summary>
    /// The authorization server's side of a whole login: the pushed authorization, the token
    /// exchange (tokens for Alice) and revocation, at <see cref="Issuer"/>.
    /// </summary>
    public static HttpResponseMessage AuthorizationServer(StubRequest request) => request.Path switch
    {
        "/oauth/par" => ParResponse(),
        "/oauth/token" => TokenResponse("at-1", "rt-1"),
        "/oauth/revoke" => new HttpResponseMessage(HttpStatusCode.OK),
        _ => throw new InvalidOperationException($"Unexpected request to {request.Uri}"),
    };

    /// <summary>The callback URL the kit's clients register.</summary>
    public const string RedirectUri = "https://app.example.com/callback";

    /// <summary>
    /// Runs a whole login against the stub from Alice's handle, and returns the session and the
    /// authorization it started.
    /// </summary>
    public static async Task<(OAuthSession Session, OAuthAuthorizationRequest Authorization)> SignInAsync(OAuthClient client)
    {
        var authorization = await client.StartAuthorizationAsync(AliceHandle.Value, RedirectUri);
        var session = await client.CompleteAuthorizationAsync("code", authorization.State, Issuer);
        return (session, authorization);
    }

    public static PasswordSession PasswordSession(string accessJwt, string refreshJwt, Uri? service = null) => new()
    {
        Did = Alice,
        Handle = AliceHandle,
        ServiceEndpoint = service ?? Pds,
        AccessJwt = accessJwt,
        RefreshJwt = refreshJwt,
        ExpiresAt = SessionManager.ReadJwtExpiry(accessJwt),
    };

    public static OAuthSession OAuthSession(
        byte[] dpopKey, string accessToken = "at-1", string? refreshToken = "rt-1",
        DateTimeOffset? expiresAt = null, Uri? revocationEndpoint = null) => new()
    {
        Did = Alice,
        Handle = AliceHandle,
        ServiceEndpoint = Pds,
        AccessToken = accessToken,
        RefreshToken = refreshToken,
        DPoPKey = dpopKey,
        Issuer = Issuer,
        TokenEndpoint = TokenEndpoint,
        RevocationEndpoint = revocationEndpoint,
        ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(15),
        Scope = "atproto transition:generic",
    };

    public static byte[] NewDPoPKey()
    {
        using var key = new DPoPProofGenerator();
        return key.ExportPrivateKey();
    }

    /// <summary>
    /// A public client over <paramref name="httpClient"/>, resolving identities with
    /// <paramref name="identity"/> (default <see cref="AliceIdentity"/>), with DPoP nonces of its own
    /// so no other test's nonces reach it.
    /// </summary>
    public static OAuthClient OAuthClient(HttpClient httpClient, IIdentityResolver? identity = null) => new(
        new OAuthOptions
        {
            ClientMetadata = new OAuthClientMetadata
            {
                ClientId = ClientId,
                RedirectUris = [RedirectUri],
            },

            // The stub stands in for the PDS and the authorization server.
            HttpClient = httpClient,
            IdentityResolver = identity ?? AliceIdentity(),
        },
        NullLogger.Instance)
    {
        NonceCache = new DPoPNonceCache(),
    };

    public static AtProtoClient Client(
        HttpClient httpClient,
        IAtProtoSessionStore? store = null,
        Action<AtProtoClientOptions>? configure = null,
        TimeProvider? time = null,
        string instanceUrl = "https://pds.example.com")
    {
        var options = new AtProtoClientOptions { InstanceUrl = instanceUrl };
        configure?.Invoke(options);
        return new AtProtoClient(options, httpClient, store, null, time ?? TimeProvider.System);
    }

    /// <summary>Waits until <paramref name="condition"/> holds, for asynchronous effects.</summary>
    public static async Task Eventually(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("The condition did not hold in time.");
            await Task.Delay(10);
        }
    }
}

/// <summary>A clock whose timers fire only when the test says so.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly List<ManualTimer> _timers = [];

    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<ManualTimer> ActiveTimers
    {
        get
        {
            lock (_timers)
                return [.. _timers.Where(t => !t.Disposed)];
        }
    }

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(callback, state, dueTime);
        lock (_timers)
            _timers.Add(timer);
        return timer;
    }

    internal sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
    {
        public TimeSpan DueTime { get; private set; } = dueTime;

        public bool Disposed { get; private set; }

        public void Fire() => callback(state);

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            DueTime = dueTime;
            return !Disposed;
        }

        public void Dispose() => Disposed = true;

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
