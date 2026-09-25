using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Auth.OAuth;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging.Abstractions;

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
internal sealed class StubServer : HttpMessageHandler
{
    private readonly ConcurrentQueue<StubRequest> _requests = new();

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

    public static OAuthClient OAuthClient(HttpClient httpClient) => new(
        new OAuthOptions
        {
            ClientMetadata = new OAuthClientMetadata
            {
                ClientId = ClientId,
                RedirectUris = ["https://app.example.com/callback"],
            },
        },
        httpClient,
        NullLogger.Instance);

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
