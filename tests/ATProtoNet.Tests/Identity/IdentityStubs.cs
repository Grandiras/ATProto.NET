using System.Collections.Concurrent;
using System.Text;
using ATProtoNet.Identity;
using Microsoft.Extensions.Caching.Distributed;

namespace ATProtoNet.Tests.Identity;

/// <summary>Minimal DID documents.</summary>
public static class Docs
{
    public static string Json(string did, string? handle = null, string? pds = "https://pds.example.com", string? signingKey = null)
    {
        var aka = handle is null ? "[]" : $"[\"at://{handle}\"]";
        var methods = signingKey is null ? "[]" : $$"""[{"id":"{{did}}#atproto","type":"Multikey","controller":"{{did}}","publicKeyMultibase":"{{signingKey.Replace("did:key:", "")}}"}]""";
        var services = pds is null ? "[]" : $$"""[{"id":"#atproto_pds","type":"AtprotoPersonalDataServer","serviceEndpoint":"{{pds}}"}]""";
        return $$"""{"id":"{{did}}","alsoKnownAs":{{aka}},"verificationMethod":{{methods}},"service":{{services}}}""";
    }

    public static DidDocument Parse(string did, string? handle = null, string? pds = "https://pds.example.com", string? signingKey = null) =>
        DidDocument.Parse(Encoding.UTF8.GetBytes(Json(did, handle, pds, signingKey)));
}

/// <summary>A DID resolver answering from a delegate, counting and announcing its calls.</summary>
public sealed class StubDidResolver(Func<Did, Task<DidDocument>> resolve) : IDidResolver
{
    private readonly ConcurrentDictionary<Did, int> _calls = new();
    private readonly ConcurrentDictionary<(Did, int), TaskCompletionSource> _reached = new();

    public int Calls(Did did) => _calls.GetValueOrDefault(did);

    /// <summary>Completes once <paramref name="did"/> has been resolved <paramref name="count"/> times.</summary>
    public Task Reached(Did did, int count) => _reached.GetOrAdd((did, count), _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).Task.WaitAsync(TimeSpan.FromSeconds(30));

    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        var n = _calls.AddOrUpdate(did, 1, (_, c) => c + 1);
        _reached.GetOrAdd((did, n), _ => new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        return resolve(did);
    }
}

/// <summary>A handle resolver answering from a delegate.</summary>
public sealed class StubHandleResolver(Func<Handle, Task<Did?>> resolve) : IHandleResolver
{
    public List<Handle> Invalidated { get; } = [];

    public Task<Did?> ResolveAsync(Handle handle, CancellationToken cancellationToken = default) => resolve(handle);

    public ValueTask InvalidateAsync(Handle handle, CancellationToken cancellationToken = default)
    {
        Invalidated.Add(handle);
        return ValueTask.CompletedTask;
    }
}

/// <summary>An in-memory <see cref="IDistributedCache"/> whose writes can be held back or failed.</summary>
public sealed class DictionaryCache : IDistributedCache
{
    private readonly ConcurrentDictionary<string, byte[]> _entries = new();

    public Exception? Fail { get; set; }

    public TaskCompletionSource? HoldWrites { get; set; }

    public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource WriteLanded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource? HoldReads { get; set; }

    public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public byte[]? Get(string key) => Fail is { } fail ? throw fail : _entries.GetValueOrDefault(key);

    public async Task<byte[]?> GetAsync(string key, CancellationToken token = default)
    {
        var value = Get(key);
        ReadStarted.TrySetResult();
        if (HoldReads is { } hold) await hold.Task;
        return value;
    }

    public void Refresh(string key) { }

    public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;

    public void Remove(string key) => _entries.TryRemove(key, out _);

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        Remove(key);
        return Task.CompletedTask;
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value;

    public async Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        WriteStarted.TrySetResult();
        if (HoldWrites is { } hold) await hold.Task;
        if (Fail is { } fail) throw fail;
        Set(key, value, options);
        WriteLanded.TrySetResult();
    }
}
