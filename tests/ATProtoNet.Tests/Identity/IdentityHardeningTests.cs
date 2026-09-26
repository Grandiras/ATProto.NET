using System.Net;
using System.Text;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Caching.Distributed;

namespace ATProtoNet.Tests.Identity;

/// <summary>
/// Regressions for the adversarial review of #120: every way a host can misbehave surfaces as a
/// <see cref="DidResolutionException"/> (and is remembered as one), documents with <c>null</c>
/// where DID Core requires values cannot poison lookups, the refresh floor holds after a failed
/// refresh, redirects are not followed, invalidation wins over a late distributed write, lookups
/// follow the reference implementation, and fetches are bounded.
/// </summary>
public class IdentityHardeningTests
{
    private static readonly IdentityResolverOptions DevOptions = new() { AllowPrivateNetworks = true };

    private static DidDocument Parse(string json) =>
        JsonSerializer.Deserialize<DidDocument>(json, AtProtoJsonDefaults.Options)!;

    private static string Http(string status, string headers, string body) =>
        $"HTTP/1.1 {status}\r\n{headers}Content-Length: {Encoding.Latin1.GetByteCount(body)}\r\nConnection: close\r\n\r\n{body}";

    // ── A host that breaks off or sends garbage ──────────────

    [Fact]
    public async Task DidWeb_BodyCutShort_IsANetworkErrorAndIsRemembered()
    {
        // Content-Length promises 1000 bytes; the host sends six and hangs up.
        using var server = new LoopbackServer(_ =>
            "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 1000\r\nConnection: close\r\n\r\n{\"id\":");
        using var web = new DidWebResolver(DevOptions);
        using var cache = new CachingDidResolver(web);
        var did = Did.Parse($"did:web:localhost%3A{server.Port}");

        var errors = new List<DidResolutionException>();
        for (var i = 0; i < 3; i++)
            errors.Add(await Assert.ThrowsAsync<DidResolutionException>(() => cache.ResolveAsync(did)));

        Assert.All(errors, e => Assert.Equal(DidResolutionErrorKind.NetworkError, e.Kind));
        Assert.Equal(1, server.Connections);
    }

    [Theory]
    [InlineData("gzip")]
    [InlineData("deflate")]
    [InlineData("br")]
    public async Task DidWeb_BodyThatDoesNotDecode_IsAnInvalidDocument(string encoding)
    {
        using var server = new LoopbackServer(_ => Http(
            "200 OK", $"Content-Type: application/json\r\nContent-Encoding: {encoding}\r\n", "\x1f\x8b\x08\x00garbagegarbagegarbage"));
        using var web = new DidWebResolver(DevOptions);

        var ex = await Assert.ThrowsAsync<DidResolutionException>(
            () => web.ResolveAsync(Did.Parse($"did:web:localhost%3A{server.Port}")));

        Assert.Equal(DidResolutionErrorKind.InvalidDocument, ex.Kind);
    }

    [Fact]
    public async Task HandleResolver_WellKnownBodyCutShort_IsNoAnswer()
    {
        var handler = new ScriptedHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new BrokenStream()) });
        using var handles = new HandleResolver(new HttpClient(handler), new IdentityResolverOptions { DnsOverHttpsUrl = null });

        Assert.Null(await handles.ResolveAsync(Handle.Parse("alice.example.com")));
    }

    [Fact]
    public async Task IdentityResolver_HandleResolverThatThrows_IsHandleInvalidNotAFailure()
    {
        var did = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");
        var dids = new StaticResolver(DidDocs.Parse(did.Value, handle: "alice.example.com"));
        using var identity = new IdentityResolver(dids, new ThrowingHandleResolver(new IOException("reset")));

        var resolved = await identity.ResolveAsync(AtIdentifier.FromDid(did));

        Assert.Equal(Handle.Invalid, resolved.Handle);
        Assert.False(resolved.HandleVerified);
    }

    [Fact]
    public async Task IdentityResolver_HandleInputWhoseResolverThrows_IsHandleNotFound()
    {
        using var identity = new IdentityResolver(new StaticResolver(null), new ThrowingHandleResolver(new IOException("reset")));

        var ex = await Assert.ThrowsAsync<DidResolutionException>(
            () => identity.ResolveAsync(AtIdentifier.Parse("alice.example.com")));

        Assert.Equal(DidResolutionErrorKind.HandleNotFound, ex.Kind);
    }

    // ── null where DID Core requires values ──────────────────

    [Theory]
    [InlineData("""{"id":"did:web:x.com","alsoKnownAs":null}""")]
    [InlineData("""{"id":"did:web:x.com","verificationMethod":null}""")]
    [InlineData("""{"id":"did:web:x.com","service":null}""")]
    public void DidDocument_NullList_ReadsAsEmptyAndLookupsWork(string json)
    {
        var document = Parse(json);

        Assert.Null(document.GetHandle());
        Assert.Null(document.GetSigningKey());
        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(DidDocumentEntryStatus.Absent, document.TryGetServiceEndpoint("#atproto_pds", null, out _));
    }

    [Theory]
    [InlineData("""{"id":"did:web:x.com","alsoKnownAs":[null]}""")]
    [InlineData("""{"id":"did:web:x.com","verificationMethod":[null]}""")]
    [InlineData("""{"id":"did:web:x.com","service":[null]}""")]
    [InlineData("""{"id":"did:web:x.com","service":"https://x.com"}""")]
    public void DidDocument_NullOrMisshapenEntry_FailsToParse(string json) =>
        Assert.Throws<JsonException>(() => Parse(json));

    [Fact]
    public async Task DidWeb_DocumentWithANullEntry_IsAnInvalidDocument()
    {
        var handler = new ScriptedHandler(_ => ScriptedHandler.Json("""{"id":"did:web:example.com","service":[null]}"""));
        using var web = new DidWebResolver(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => web.ResolveAsync(Did.Parse("did:web:example.com")));

        Assert.Equal(DidResolutionErrorKind.InvalidDocument, ex.Kind);
    }

    [Fact]
    public void DidDocument_NullAssignedInCode_ReadsAsEmpty()
    {
        var document = new DidDocument { Id = Did.Parse("did:web:x.com"), AlsoKnownAs = null!, Service = null! };

        Assert.Empty(document.AlsoKnownAs);
        Assert.Null(document.GetPdsEndpoint());
    }

    // ── The refresh floor after a failure ────────────────────

    [Fact]
    public async Task RefreshAsync_AfterAFailedRefresh_TheFloorStillHolds()
    {
        var did = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");
        var clock = new ManualClock();
        var calls = 0;
        var inner = new DelegateResolver(d => ++calls == 1
            ? Task.FromResult(DidDocs.Parse(d.Value))
            : Task.FromException<DidDocument>(new DidResolutionException("boom", DidResolutionErrorKind.HttpError, d)));
        using var cache = new CachingDidResolver(inner, new DidCacheOptions(), null, clock);

        await cache.ResolveAsync(did);
        clock.Advance(TimeSpan.FromSeconds(31));
        for (var i = 0; i < 10; i++)
        {
            try
            {
                await cache.RefreshAsync(did);
            }
            catch (DidResolutionException)
            {
                // The first refresh fails; the stale document is served for the rest.
            }

            clock.Advance(TimeSpan.FromSeconds(1));
        }

        // The resolve, then one refresh: the other nine fall inside the 30-second floor.
        Assert.Equal(2, calls);
    }

    // ── Redirects ────────────────────────────────────────────

    [Fact]
    public async Task DidWeb_RedirectToAnotherOrigin_IsRefusedWithoutFollowing()
    {
        using var target = new LoopbackServer(port => Http("200 OK", "Content-Type: application/json\r\n", $$"""{"id":"did:web:localhost%3A{{port}}"}"""));
        using var origin = new LoopbackServer(_ =>
            $"HTTP/1.1 302 Found\r\nLocation: http://127.0.0.1:{target.Port}/anything\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        using var web = new DidWebResolver(DevOptions);

        var ex = await Assert.ThrowsAsync<DidResolutionException>(
            () => web.ResolveAsync(Did.Parse($"did:web:localhost%3A{origin.Port}")));

        Assert.Equal(DidResolutionErrorKind.HttpError, ex.Kind);
        Assert.Equal(0, target.Connections);
    }

    [Fact]
    public async Task DidWeb_CallersClientThatFollowedARedirect_IsRefused()
    {
        var handler = new ScriptedHandler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DidDocs.Json("did:web:example.com")),
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://elsewhere.example.net/did.json"),
        });
        using var web = new DidWebResolver(new HttpClient(handler));

        var ex = await Assert.ThrowsAsync<DidResolutionException>(() => web.ResolveAsync(Did.Parse("did:web:example.com")));

        Assert.Equal(DidResolutionErrorKind.HttpError, ex.Kind);
    }

    [Theory]
    [InlineData("https://alice.example.com/.well-known/atproto-did/", true)]
    [InlineData("https://ALICE.example.com/other", true)]
    [InlineData("https://attacker.example.net/.well-known/atproto-did", false)]
    [InlineData("http://alice.example.com/.well-known/atproto-did", false)]
    public async Task HandleWellKnown_OnlySameHostHttpsRedirectsAreFollowed(string location, bool followed)
    {
        var did = Did.Parse("did:plc:aaaaaaaaaaaaaaaaaaaaaaaa");
        var handler = new ScriptedHandler(request => request.RequestUri!.AbsolutePath == "/.well-known/atproto-did"
            ? new HttpResponseMessage(HttpStatusCode.Found) { Headers = { Location = new Uri(location) }, Content = new StringContent("") }
            : ScriptedHandler.Text(did.Value));
        using var handles = new HandleResolver(new HttpClient(handler), new IdentityResolverOptions { DnsOverHttpsUrl = null });

        var resolved = await handles.ResolveAsync(Handle.Parse("alice.example.com"));

        Assert.Equal(followed ? did : null, resolved);
        Assert.Equal(followed ? 2 : 1, handler.Count);
    }

    // ── Invalidation against a late distributed write ────────

    [Fact]
    public async Task InvalidateAsync_WhileTheDistributedWriteIsInFlight_TheOldDocumentDoesNotSurvive()
    {
        var did = Did.Parse("did:plc:ewvi7nxzyoun6zhxrhs64oiz");
        var shared = new GatedDistributedCache();
        var oldKey = "did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w";
        using var cache = new CachingDidResolver(
            new StaticResolver(DidDocs.Parse(did.Value, signingKey: oldKey)), new DidCacheOptions(), shared);

        await cache.ResolveAsync(did);     // stored in memory; the shared write is held back
        await cache.InvalidateAsync(did);  // an #identity event: memory and shared copy dropped
        shared.Release();                  // the old write lands afterwards
        await shared.Settled;

        Assert.Null(await shared.GetAsync("atproto:did:" + did.Value));
    }

    // ── Lookups as the reference implementation does them ────

    [Fact]
    public void Lookups_WithDuplicateIds_TakeTheFirstEntry()
    {
        var document = Parse("""
            {"id":"did:web:example.com",
             "verificationMethod":[
               {"id":"#atproto","type":"Multikey","publicKeyMultibase":"zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w"},
               {"id":"did:web:example.com#atproto","type":"Multikey","publicKeyMultibase":"zQ3shXjHeiBuRCKmM36cuYnm7YEMzhGnCmCyW92sRJ9pribSF"}],
             "service":[
               {"id":"#atproto_pds","type":"Other","serviceEndpoint":"https://first.example"},
               {"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"https://second.example"}]}
            """);

        Assert.Equal("did:key:zQ3shunBKsXixLxKtC5qeSG9E4J5RkGN57im31pcTzbNQnm5w", document.GetSigningKey());
        Assert.Null(document.GetPdsEndpoint());
        Assert.Equal(DidDocumentEntryStatus.Malformed, document.TryGetServiceEndpoint("#atproto_pds", "AtprotoPersonalDataServer", out _));
    }

    // ── Bounds ───────────────────────────────────────────────

    [Fact]
    public async Task Failures_DoNotEvictDocuments()
    {
        var good = Enumerable.Range(0, 2).Select(i => Did.Parse($"did:plc:good{(char)('a' + i)}aaaaaaaaaaaaaaaaaaa")).ToArray();
        var calls = new Dictionary<Did, int>();
        var inner = new DelegateResolver(d =>
        {
            lock (calls)
                calls[d] = calls.GetValueOrDefault(d) + 1;
            return good.Contains(d)
                ? Task.FromResult(DidDocs.Parse(d.Value))
                : Task.FromException<DidDocument>(new DidResolutionException("no", DidResolutionErrorKind.NotFound, d));
        });
        using var cache = new CachingDidResolver(inner, new DidCacheOptions { Capacity = 2, FailureCapacity = 3 });

        foreach (var did in good)
            await cache.ResolveAsync(did);
        for (var i = 0; i < 50; i++)
            await Assert.ThrowsAsync<DidResolutionException>(() => cache.ResolveAsync(Did.Parse($"did:web:bogus{i}.example.com")));
        foreach (var did in good)
            await cache.ResolveAsync(did);

        Assert.All(good, did => Assert.Equal(1, calls[did]));
        Assert.Equal(2, cache.DocumentCount);
        Assert.Equal(5, cache.Count);
    }

    [Fact]
    public async Task Fetches_AreCappedProcessWide()
    {
        var inFlight = 0;
        var peak = 0;
        var handler = new ScriptedHandler(async (_, ct) =>
        {
            var now = Interlocked.Increment(ref inFlight);
            InterlockedMax(ref peak, now);
            await Task.Delay(50, ct);
            Interlocked.Decrement(ref inFlight);
            return ScriptedHandler.Json("{}");
        });
        using var client = new HttpClient(handler);

        var fetches = Enumerable.Range(0, IdentityFetch.MaxConcurrentFetches * 2).Select(i => IdentityFetch.GetAsync(
            client, new Uri($"https://host{i}.example.com/"), "*/*", 1024, TimeSpan.FromSeconds(30), did: null, default));
        await Task.WhenAll(fetches);

        Assert.InRange(peak, 1, IdentityFetch.MaxConcurrentFetches);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value &&
               Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private sealed class StaticResolver(DidDocument? document) : IDidResolver
    {
        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) =>
            document is null
                ? Task.FromException<DidDocument>(new DidResolutionException("none", DidResolutionErrorKind.NotFound, did))
                : Task.FromResult(document);
    }

    private sealed class DelegateResolver(Func<Did, Task<DidDocument>> resolve) : IDidResolver
    {
        public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default) => resolve(did);
    }

    private sealed class ThrowingHandleResolver(Exception exception) : IHandleResolver
    {
        public Task<Did?> ResolveAsync(Handle handle, CancellationToken cancellationToken = default) =>
            Task.FromException<Did?>(exception);
    }

    /// <summary>A body that fails the way a connection closed mid-response does.</summary>
    private sealed class BrokenStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw Broken();
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw Broken();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private static HttpIOException Broken() => new(HttpRequestError.ResponseEnded, "The response ended prematurely.");
    }

    /// <summary>A distributed cache whose writes wait until released.</summary>
    private sealed class GatedDistributedCache : IDistributedCache
    {
        private readonly Dictionary<string, byte[]> _data = new();
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _removes;

        /// <summary>Completes once a write has landed and the remove that undoes it followed.</summary>
        public Task Settled => _settled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release() => _gate.TrySetResult();

        public byte[]? Get(string key)
        {
            lock (_data)
                return _data.GetValueOrDefault(key);
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));

        public void Refresh(string key) { }

        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

        public void Remove(string key)
        {
            lock (_data)
                _data.Remove(key);

            // The invalidation's own remove, then the one undoing the late write.
            if (Interlocked.Increment(ref _removes) == 2)
                _settled.TrySetResult();
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            lock (_data)
                _data[key] = value;
        }

        public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            await _gate.Task;
            Set(key, value, options);
        }
    }
}
