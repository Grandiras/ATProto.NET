using System.Text.Json;
using ATProtoNet.Auth;
using ATProtoNet.Serialization;

namespace ATProtoNet.Tests.Auth;

public class SessionTests
{
    private readonly JsonSerializerOptions _options = AtProtoJsonDefaults.Options;

    [Fact]
    public void Session_DefaultValues()
    {
        var session = new Session();

        Assert.Equal(string.Empty, session.Did);
        Assert.Equal(string.Empty, session.Handle);
        Assert.Equal(string.Empty, session.AccessJwt);
        Assert.Equal(string.Empty, session.RefreshJwt);
        Assert.Null(session.Email);
        Assert.Null(session.EmailConfirmed);
        Assert.Null(session.EmailAuthFactor);
        Assert.Null(session.DidDoc);
        Assert.Null(session.Active);
        Assert.Null(session.Status);
    }

    [Fact]
    public void Session_InitProperties()
    {
        var session = new Session
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessJwt = "access-token",
            RefreshJwt = "refresh-token",
            Email = "alice@example.com",
            EmailConfirmed = true,
            Active = true,
        };

        Assert.Equal("did:plc:abc123", session.Did);
        Assert.Equal("alice.bsky.social", session.Handle);
        Assert.Equal("access-token", session.AccessJwt);
        Assert.Equal("refresh-token", session.RefreshJwt);
        Assert.Equal("alice@example.com", session.Email);
        Assert.True(session.EmailConfirmed);
        Assert.True(session.Active);
    }

    [Fact]
    public void Session_SerializesCorrectly()
    {
        var session = new Session
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessJwt = "access",
            RefreshJwt = "refresh",
        };

        var json = JsonSerializer.Serialize(session, _options);
        var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("did:plc:abc123", root.GetProperty("did").GetString());
        Assert.Equal("alice.bsky.social", root.GetProperty("handle").GetString());
        Assert.Equal("access", root.GetProperty("accessJwt").GetString());
        Assert.Equal("refresh", root.GetProperty("refreshJwt").GetString());
    }

    [Fact]
    public void Session_DeserializesCorrectly()
    {
        var json = """
        {
            "did": "did:plc:abc123",
            "handle": "alice.bsky.social",
            "accessJwt": "access",
            "refreshJwt": "refresh",
            "email": "alice@example.com",
            "emailConfirmed": true
        }
        """;

        var session = JsonSerializer.Deserialize<Session>(json, _options);

        Assert.NotNull(session);
        Assert.Equal("did:plc:abc123", session.Did);
        Assert.Equal("alice.bsky.social", session.Handle);
        Assert.Equal("access", session.AccessJwt);
        Assert.Equal("refresh", session.RefreshJwt);
        Assert.Equal("alice@example.com", session.Email);
        Assert.True(session.EmailConfirmed);
    }

    [Fact]
    public void Session_RoundTrips()
    {
        var original = new Session
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessJwt = "access",
            RefreshJwt = "refresh",
            Email = "test@example.com",
            EmailConfirmed = true,
            Active = true,
        };

        var json = JsonSerializer.Serialize(original, _options);
        var deserialized = JsonSerializer.Deserialize<Session>(json, _options);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Did, deserialized.Did);
        Assert.Equal(original.Handle, deserialized.Handle);
        Assert.Equal(original.AccessJwt, deserialized.AccessJwt);
        Assert.Equal(original.RefreshJwt, deserialized.RefreshJwt);
        Assert.Equal(original.Email, deserialized.Email);
        Assert.Equal(original.EmailConfirmed, deserialized.EmailConfirmed);
        Assert.Equal(original.Active, deserialized.Active);
    }

    [Fact]
    public void Session_NullOptionalFields_OmittedFromJson()
    {
        var session = new Session
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessJwt = "access",
            RefreshJwt = "refresh",
        };

        var json = JsonSerializer.Serialize(session, _options);

        Assert.DoesNotContain("email", json);
        Assert.DoesNotContain("emailConfirmed", json);
        Assert.DoesNotContain("emailAuthFactor", json);
    }
}

public class InMemorySessionStoreTests
{
    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = new InMemorySessionStore();
        var session = new Session
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessJwt = "access",
            RefreshJwt = "refresh",
        };

        await store.SaveAsync(session);
        var loaded = await store.LoadAsync();

        Assert.NotNull(loaded);
        Assert.Equal(session.Did, loaded.Did);
        Assert.Equal(session.Handle, loaded.Handle);
    }

    [Fact]
    public async Task Load_WhenEmpty_ReturnsNull()
    {
        var store = new InMemorySessionStore();
        var loaded = await store.LoadAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Clear_RemovesSession()
    {
        var store = new InMemorySessionStore();
        var session = new Session
        {
            Did = "did:plc:abc123",
            Handle = "alice.bsky.social",
            AccessJwt = "access",
            RefreshJwt = "refresh",
        };

        await store.SaveAsync(session);
        await store.ClearAsync();
        var loaded = await store.LoadAsync();

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Save_OverwritesPrevious()
    {
        var store = new InMemorySessionStore();

        await store.SaveAsync(new Session
        {
            Did = "did:plc:first",
            Handle = "first.bsky.social",
            AccessJwt = "a",
            RefreshJwt = "b",
        });

        await store.SaveAsync(new Session
        {
            Did = "did:plc:second",
            Handle = "second.bsky.social",
            AccessJwt = "c",
            RefreshJwt = "d",
        });

        var loaded = await store.LoadAsync();
        Assert.NotNull(loaded);
        Assert.Equal("did:plc:second", loaded.Did);
    }
}
