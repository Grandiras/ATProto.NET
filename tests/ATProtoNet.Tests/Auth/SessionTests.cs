using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Identity;
using static ATProtoNet.Tests.Auth.SessionKit;

namespace ATProtoNet.Tests.Auth;

public class SessionTests
{
    [Fact]
    public void PasswordSession_SerializesAsTheBaseTypeWithItsKind()
    {
        AtProtoSession session = PasswordSession("access", "refresh") with { Email = "alice@example.com" };

        var json = JsonSerializer.Serialize(session);
        var root = JsonDocument.Parse(json).RootElement;

        Assert.Equal("password", root.GetProperty("$kind").GetString());
        Assert.Equal("did:plc:alice", root.GetProperty("did").GetString());
        Assert.Equal("alice.test", root.GetProperty("handle").GetString());
        Assert.Equal("https://pds.example.com/", root.GetProperty("serviceEndpoint").GetString());
        Assert.Equal("access", root.GetProperty("accessJwt").GetString());
        Assert.Equal("refresh", root.GetProperty("refreshJwt").GetString());
        Assert.Equal(session, JsonSerializer.Deserialize<AtProtoSession>(json));
    }

    [Fact]
    public void OAuthSession_RoundTripsWithItsKey()
    {
        var key = NewDPoPKey();
        AtProtoSession session = OAuthSession(key, revocationEndpoint: RevocationEndpoint);

        var json = JsonSerializer.Serialize(session);
        var read = Assert.IsType<OAuthSession>(JsonSerializer.Deserialize<AtProtoSession>(json));

        Assert.Equal("oauth", JsonDocument.Parse(json).RootElement.GetProperty("$kind").GetString());
        Assert.Equal(key, read.DPoPKey.ToArray());
        Assert.Equal(RevocationEndpoint, read.RevocationEndpoint);
        Assert.Equal(TokenEndpoint, read.TokenEndpoint);
        Assert.Equal("at-1", read.AccessToken);
        Assert.Equal("rt-1", read.RefreshToken);
        Assert.Equal(((OAuthSession)session).ExpiresAt, read.ExpiresAt);
    }

    [Fact]
    public void OAuthSession_ClientKeyId_RoundTrips()
    {
        AtProtoSession session = OAuthSession(NewDPoPKey()) with { ClientKeyId = "key-2026" };

        var json = JsonSerializer.Serialize(session);
        var read = Assert.IsType<OAuthSession>(JsonSerializer.Deserialize<AtProtoSession>(json));

        Assert.Equal("key-2026", JsonDocument.Parse(json).RootElement.GetProperty("clientKeyId").GetString());
        Assert.Equal("key-2026", read.ClientKeyId);
    }

    [Fact]
    public void OAuthSession_StoredWithoutAClientKeyId_ReadsAsAPublicClientSession()
    {
        // What a session store holds from before confidential clients: no clientKeyId member.
        var stored = JsonSerializer.Serialize<AtProtoSession>(OAuthSession(NewDPoPKey()));
        var root = System.Text.Json.Nodes.JsonNode.Parse(stored)!.AsObject();
        root.Remove("clientKeyId");

        var read = Assert.IsType<OAuthSession>(AtProtoSessionJson.Deserialize(root.ToJsonString()));

        Assert.Null(read.ClientKeyId);
        Assert.Equal("at-1", read.AccessToken);
    }

    [Fact]
    public void ToString_NamesTheAccountButNotItsCredentials()
    {
        var password = PasswordSession("secret-access", "secret-refresh").ToString();
        var oauth = OAuthSession(NewDPoPKey(), accessToken: "secret-at", refreshToken: "secret-rt").ToString();

        Assert.Equal("PasswordSession { Did = did:plc:alice, Handle = alice.test }", password);
        Assert.DoesNotContain("secret", oauth);
    }

    [Fact]
    public void ReadJwtExpiry_ReadsTheExpClaim()
    {
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(1_900_000_000);

        Assert.Equal(expiresAt, SessionManager.ReadJwtExpiry(AccessJwt("a", expiresAt)));
        Assert.Null(SessionManager.ReadJwtExpiry("not-a-jwt"));
        Assert.Null(SessionManager.ReadJwtExpiry(TestJws.Mint(new { alg = "HS256" }, new { sub = "x" }, _ => new byte[32])));
    }

    // ──────────────────────────────────────────────────────────
    //  The persisted form, and what the 0.6 token stores wrote
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void StoredJson_RoundTripsBothKinds()
    {
        var password = PasswordSession("access", "refresh");
        var oauth = OAuthSession(NewDPoPKey());

        Assert.Equal(password, AtProtoSessionJson.Deserialize(AtProtoSessionJson.Serialize(password)));
        var read = Assert.IsType<OAuthSession>(AtProtoSessionJson.Deserialize(AtProtoSessionJson.Serialize(oauth)));
        Assert.Equal(oauth with { DPoPKey = read.DPoPKey }, read);
    }

    [Fact]
    public void StoredJson_TokenDataFrom06_ReadsAsAnOAuthSession()
    {
        var key = NewDPoPKey();
        var obtainedAt = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var legacy = Legacy06TokenData.Serialize(new Legacy06TokenData
        {
            Did = "did:plc:alice",
            Handle = "alice.test",
            IsHandleVerified = true,
            AccessToken = "at-1",
            RefreshToken = "rt-1",
            PdsUrl = "https://pds.example.com",
            Issuer = "https://auth.example.com",
            TokenEndpoint = "https://auth.example.com/oauth/token",
            DPoPPrivateKey = key,
            AuthServerDpopNonce = "as-nonce",
            ResourceServerDpopNonce = "rs-nonce",
            TokenObtainedAt = obtainedAt,
            ExpiresIn = 3600,
            Scope = "atproto transition:generic",
        });

        var session = Assert.IsType<OAuthSession>(AtProtoSessionJson.Deserialize(legacy));

        Assert.Equal(Alice, session.Did);
        Assert.Equal(AliceHandle, session.Handle);
        Assert.Equal(new Uri("https://pds.example.com"), session.ServiceEndpoint);
        Assert.Equal("at-1", session.AccessToken);
        Assert.Equal("rt-1", session.RefreshToken);
        Assert.Equal(key, session.DPoPKey.ToArray());
        Assert.Equal("https://auth.example.com", session.Issuer);
        Assert.Equal(TokenEndpoint, session.TokenEndpoint);
        Assert.Equal(obtainedAt.AddHours(1), session.ExpiresAt);
        Assert.Equal("atproto transition:generic", session.Scope);
        Assert.Null(session.RevocationEndpoint);
    }

    [Fact]
    public void StoredJson_UnverifiedHandleFrom06_ReadsAsHandleInvalid()
    {
        // 0.6 put the DID in place of a handle that did not verify.
        var legacy = Legacy06TokenData.Serialize(new Legacy06TokenData
        {
            Did = "did:plc:alice",
            Handle = "did:plc:alice",
            IsHandleVerified = false,
            AccessToken = "at-1",
            PdsUrl = "https://pds.example.com",
            Issuer = "https://auth.example.com",
            TokenEndpoint = "https://auth.example.com/oauth/token",
            DPoPPrivateKey = NewDPoPKey(),
        });

        var session = AtProtoSessionJson.Deserialize(legacy);

        Assert.Equal("handle.invalid", session.Handle.Value);
        Assert.Null(session.ExpiresAt);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("""{"did":"did:plc:alice"}""")]
    [InlineData("""{"$kind":"unknown","did":"did:plc:alice"}""")]
    [InlineData("""{"$kind":"password","did":"not a did","handle":"a.test","serviceEndpoint":"https://x","accessJwt":"a","refreshJwt":"r"}""")]
    [InlineData("""{"$kind":"password","did":"did:plc:alice","handle":"a.test","serviceEndpoint":"not a uri","accessJwt":"a","refreshJwt":"r"}""")]
    [InlineData("""{"$kind":"oauth","did":"did:plc:alice","handle":"a.test","serviceEndpoint":"https://x","accessToken":"a","dpopKey":"!!!","issuer":"i","tokenEndpoint":"https://x/t"}""")]
    [InlineData("""{"did":"did:plc:alice","handle":"a.test","accessToken":"a","pdsUrl":"https://x","issuer":"i","tokenEndpoint":"https://x/t","dPoPPrivateKey":"!!!"}""")]
    public void StoredJson_Garbage_ThrowsJsonException(string json)
    {
        // A store treats a JsonException as a corrupt entry; anything else would escape it.
        Assert.IsType<JsonException>(Record.Exception(() => AtProtoSessionJson.Deserialize(json)), exactMatch: false);
    }

    /// <summary>
    /// The shape of <c>AtProtoTokenData</c> in 0.6, serialized the way the 0.6 file and EF Core
    /// token stores did, so stores upgraded in place are read back faithfully.
    /// </summary>
    internal sealed class Legacy06TokenData
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public required string Did { get; init; }
        public required string Handle { get; init; }
        public bool IsHandleVerified { get; init; }
        public required string AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public required string PdsUrl { get; init; }
        public required string Issuer { get; init; }
        public required string TokenEndpoint { get; init; }
        public required byte[] DPoPPrivateKey { get; init; }
        public string? AuthServerDpopNonce { get; set; }
        public string? ResourceServerDpopNonce { get; set; }
        public DateTimeOffset TokenObtainedAt { get; set; } = DateTimeOffset.UtcNow;
        public int? ExpiresIn { get; init; }
        public string? Scope { get; init; }

        public static string Serialize(Legacy06TokenData data) => JsonSerializer.Serialize(data, Options);
    }
}

public class InMemoryAtProtoSessionStoreTests
{
    [Fact]
    public async Task SetAndGet_ReturnTheStoredSession()
    {
        var store = new InMemoryAtProtoSessionStore();
        var session = PasswordSession("access", "refresh");

        await store.SetAsync(session);

        Assert.Same(session, await store.GetAsync(Alice));
    }

    [Fact]
    public async Task Get_WhenNothingIsStored_ReturnsNull()
    {
        Assert.Null(await new InMemoryAtProtoSessionStore().GetAsync(Alice));
    }

    [Fact]
    public async Task Set_ReplacesTheAccountsPreviousSession_AndKeepsOtherAccounts()
    {
        var store = new InMemoryAtProtoSessionStore();
        var bob = PasswordSession("b", "b") with { Did = Did.Parse("did:plc:bob") };
        await store.SetAsync(PasswordSession("old", "old"));
        await store.SetAsync(bob);

        var current = PasswordSession("new", "new");
        await store.SetAsync(current);

        Assert.Same(current, await store.GetAsync(Alice));
        Assert.Same(bob, await store.GetAsync(bob.Did));
    }

    [Fact]
    public async Task Remove_DropsOnlyThatAccount()
    {
        var store = new InMemoryAtProtoSessionStore();
        var bob = PasswordSession("b", "b") with { Did = Did.Parse("did:plc:bob") };
        await store.SetAsync(PasswordSession("a", "a"));
        await store.SetAsync(bob);

        await store.RemoveAsync(Alice);
        await store.RemoveAsync(Did.Parse("did:plc:nobody"));

        Assert.Null(await store.GetAsync(Alice));
        Assert.Same(bob, await store.GetAsync(bob.Did));
    }
}
