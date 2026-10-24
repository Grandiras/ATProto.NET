using System.Net;
using ATProtoNet.Identity;
using ATProtoNet.Net;
using ATProtoNet.TestKit;
using static ATProtoNet.Tests.Identity.IdentityFixtures;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Which <c>did:web</c> identifiers turn into a request, and which handles are resolved at all, with and without the
/// development opt-out. The address rules are <see cref="Net.UntrustedFetchPolicyTests"/>.
/// </summary>
public class IdentityFetchPolicyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, bool, string?, IdentityResolutionError?> DidWebUrls => new()
    {
        { "did:web:example.com", false, "https://example.com/.well-known/did.json", null },
        { "did:web:api.bsky.app", false, "https://api.bsky.app/.well-known/did.json", null },
        { "did:web:Example.COM", false, "https://example.com/.well-known/did.json", null },
        { "did:web:xn--bcher-kva.ch", false, "https://xn--bcher-kva.ch/.well-known/did.json", null },
        // .test hosts follow the handle rule (IDN-23, IDN-30): only under the opt-out.
        { "did:web:alice.test", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.test", true, "https://alice.test/.well-known/did.json", null },
        { "did:web:example.com", true, "https://example.com/.well-known/did.json", null },

        // Localhost is a development affordance only, and the only host a port is allowed on: without the opt-out the
        // fetch layer blocks its http URL before anything is sent.
        { "did:web:localhost", false, null, IdentityResolutionError.Unavailable },
        { "did:web:localhost%3A2583", false, null, IdentityResolutionError.Unavailable },
        { "did:web:localhost", true, "http://localhost/.well-known/did.json", null },
        { "did:web:localhost%3A2583", true, "http://localhost:2583/.well-known/did.json", null },
        { "did:web:LOCALHOST%3a2583", true, "http://localhost:2583/.well-known/did.json", null },

        // A port on any other host is refused, opt-out or not.
        { "did:web:example.com%3A8443", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:example.com%3A8443", true, null, IdentityResolutionError.InvalidDid },
        { "did:web:internal.corp%3A6379", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:localhost%3A", true, null, IdentityResolutionError.InvalidDid },
        { "did:web:localhost%3A0", true, null, IdentityResolutionError.InvalidDid },
        { "did:web:localhost%3A99999", true, null, IdentityResolutionError.InvalidDid },
        { "did:web:localhost%3A%2B80", true, null, IdentityResolutionError.InvalidDid },

        // No path-based did:web in atproto.
        { "did:web:example.com:user:alice", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:example.com:user:alice", true, null, IdentityResolutionError.InvalidDid },

        // A hostname, not an address, and nothing that reshapes the URL once decoded.
        { "did:web:127.0.0.1", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:127.0.0.1", true, null, IdentityResolutionError.InvalidDid },
        { "did:web:169.254.169.254", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:example.com%2Fevil", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:evil.com%40example.com", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:example.com%23frag", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:-bad.example.com", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:bad-.example.com", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:example..com", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:intranet", true, null, IdentityResolutionError.InvalidDid },

        // The TLDs that never resolve as handles do not resolve as did:web either.
        { "did:web:alice.internal", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.local", true, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.localhost", true, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.onion", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.example", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.arpa", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.alt", false, null, IdentityResolutionError.InvalidDid },
        { "did:web:alice.invalid", false, null, IdentityResolutionError.InvalidDid },
    };

    [Theory]
    [Trait("Spec", "IDN-22")]
    [Trait("Spec", "IDN-23")]
    [Trait("Spec", "IDN-8")]
    [MemberData(nameof(DidWebUrls))]
    public async Task DidWebResolutionUrl_Table_MatchesPolicy(string did, bool allowPrivateNetworks, string? expectedUrl, IdentityResolutionError? expectedError)
    {
        var server = new FakeXrpcServer().Fallback((_, _) => Task.FromResult(Responses.Json(Document(did))));
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = allowPrivateNetworks }, handler: server);
        var resolver = new DidWebResolver(http);

        if (expectedError is { } error)
        {
            var ex = await Assert.ThrowsAsync<IdentityResolutionException>(() => resolver.ResolveAsync(Did.Parse(did), Ct));
            Assert.Equal(error, ex.Error);
            if (error == IdentityResolutionError.Unavailable) Assert.Equal(UntrustedFetchError.Blocked, Assert.IsType<UntrustedFetchException>(ex.InnerException).Error);
            Assert.Empty(server.Requests);
        }
        else
        {
            await resolver.ResolveAsync(Did.Parse(did), Ct);
            Assert.Equal(expectedUrl, Assert.Single(server.Requests).Uri.ToString());
        }
    }

    public static TheoryData<string, bool, bool> Handles => new()
    {
        { "alice.bsky.social", false, true },
        { "example.com", false, true },
        { "alice.test", false, false },
        { "alice.test", true, true },
        { "alice.local", true, false },
        { "alice.localhost", true, false },
        { "alice.internal", false, false },
        { "alice.arpa", false, false },
        { "alice.onion", false, false },
        { "alice.alt", false, false },
        { "alice.example", false, false },
        { "alice.invalid", true, false },
        { "handle.invalid", true, false },
    };

    [Theory]
    [Trait("Spec", "IDN-30")]
    [MemberData(nameof(Handles))]
    public async Task IsResolvableHandle_Table_MatchesPolicy(string handle, bool allowPrivateNetworks, bool resolvable)
    {
        var server = new FakeXrpcServer().Fallback((_, _) => Task.FromResult(Responses.Status(HttpStatusCode.NotFound)));
        using var http = new UntrustedHttp(new() { AllowPrivateNetworks = allowPrivateNetworks }, handler: server);
        var dns = new TxtResolver((_, _) => Task.FromResult<IReadOnlyList<string>?>([]));

        Assert.Null(await new HandleResolver(http, dns).ResolveAsync(Handle.Parse(handle), Ct));

        Assert.Equal(resolvable ? [$"_atproto.{handle}"] : [], dns.Names);
        Assert.Equal(resolvable ? [new Uri($"https://{handle}/.well-known/atproto-did")] : [], server.Requests.Select(r => r.Uri));
    }
}
