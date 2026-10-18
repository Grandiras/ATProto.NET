using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Streaming;

namespace ATProtoNet.Conformance.Legacy;

public sealed partial class LegacyAdapter
{
    private const string Frames = "ATProtoNet.Streaming.EventStreamFrame, ATProtoNet";
    private const string Parser = "ATProtoNet.Streaming.FirehoseEventParser, ATProtoNet";

    /// <inheritdoc />
    /// <remarks>What 0.8's firehose codec does with a frame (<c>EventStreamLoop.Codec.ReadAsync</c> and <c>TypedFirehoseConsumer.HandleAsync</c>).</remarks>
    public JsonObject ParseFirehoseFrame(byte[] frame)
    {
        if (!TryReadHeader(null, frame, out var op, out var type, out var offset))
            return Dropped("malformed", null);

        var body = frame.AsMemory(offset);
        if (op == -1)
        {
            var error = ReadError(null, body) ?? new EventStreamError("Unknown", null);
            return new() { ["kind"] = "error", ["error"] = error.Error, ["message"] = error.Message };
        }

        if (op != 1 || string.IsNullOrEmpty(type))
            return Dropped("malformed", ReadSeq(null, body));

        return ParseBody(null, type, body) switch
        {
            null => Dropped(IsKnownType(null, type) ? "malformed" : "unknownType", ReadSeq(null, body)),
            InfoEvent info => new() { ["kind"] = "info", ["name"] = info.Name, ["message"] = info.Message },
            FirehoseEvent evt => Project(type, evt),
            var other => throw new InvalidOperationException($"Unexpected {other.GetType()}."),
        };
    }

    /// <inheritdoc />
    public SyncVerdict ApplySyncEvent(byte[] frame, SyncState? state, string? signingKey, DateTimeOffset now)
    {
        if (!TryReadHeader(null, frame, out _, out var type, out var offset) || type is null)
            return new(SyncOutcome.Invalid, state);
        var evt = ParseBody(null, type, frame.AsMemory(offset));
        if (evt is not (CommitEvent or SyncEvent))
            return new(SyncOutcome.Invalid, state);

        var did = evt is CommitEvent c ? c.Repo : ((SyncEvent)evt).Did;
        var store = new InMemoryRepoSyncStateStore();
        if (state is not null)
            store.SetAsync(new(did, state.Rev is null ? null : Tid.Parse(state.Rev), state.Data is null ? null : Cid.Parse(state.Data),
                state.Synchronized ? RepoSyncStatus.Synchronized : RepoSyncStatus.Desynchronized)).AsTask().GetAwaiter().GetResult();

        using var verifier = new RepoSyncVerifier(new RepoSyncVerifierOptions
        {
            StateStore = store,
            DidResolver = new FakeDidResolver().PublishSigningKey(did.Value, signingKey),
            TimeProvider = new FixedTime(now),
        });
        var result = (evt is CommitEvent commit ? verifier.VerifyCommitAsync(commit) : verifier.VerifySyncAsync((SyncEvent)evt)).AsTask().GetAwaiter().GetResult();
        verifier.ApplyAsync(result).AsTask().GetAwaiter().GetResult();

        var stored = store.GetAsync(did).AsTask().GetAwaiter().GetResult();
        var outcome = result.Outcome switch
        {
            RepoSyncOutcome.Valid => SyncOutcome.Valid,
            RepoSyncOutcome.Stale => SyncOutcome.Stale,
            RepoSyncOutcome.Desynchronized => SyncOutcome.Desynchronized,
            _ => SyncOutcome.Invalid,
        };
        return new(outcome, stored is null ? null : new(stored.Rev?.Value, stored.Data?.Value, stored.Status == RepoSyncStatus.Synchronized));
    }

    private static JsonObject Dropped(string reason, long? seq)
    {
        var result = new JsonObject { ["kind"] = "dropped", ["reason"] = reason };
        if (seq is not null)
            result["seq"] = seq;
        return result;
    }

    private static JsonObject Project(string type, FirehoseEvent evt)
    {
        var result = new JsonObject { ["kind"] = "event", ["type"] = type, ["seq"] = evt.Seq, ["time"] = evt.Time?.ToString() };
        switch (evt)
        {
            case CommitEvent commit:
                result["repo"] = commit.Repo.Value;
                result["commit"] = commit.Commit.Value;
                result["rev"] = commit.Rev.Value;
                result["since"] = commit.Since?.Value;
                result["prevData"] = commit.PrevData?.Value;
                result["ops"] = new JsonArray([.. (commit.Ops ?? []).Select(op => new JsonObject
                {
                    ["action"] = op.Action.ToString().ToLowerInvariant(),
                    ["path"] = op.Path,
                    ["cid"] = op.Cid?.Value,
                })]);
                break;
            case SyncEvent sync:
                result["did"] = sync.Did.Value;
                result["rev"] = sync.Rev.Value;
                break;
            case IdentityEvent identity:
                result["did"] = identity.Did.Value;
                result["handle"] = identity.Handle?.Value;
                break;
            case AccountEvent account:
                result["did"] = account.Did.Value;
                result["active"] = account.Active;
                result["status"] = account.Status;
                break;
        }

        return result;
    }

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "TryReadHeader")]
    private static extern bool TryReadHeader([UnsafeAccessorType(Frames)] object? _, ReadOnlyMemory<byte> frame, out int op, out string? type, out int bodyOffset);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ReadError")]
    private static extern EventStreamError? ReadError([UnsafeAccessorType(Frames)] object? _, ReadOnlyMemory<byte> body);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ReadSeq")]
    private static extern long? ReadSeq([UnsafeAccessorType(Frames)] object? _, ReadOnlyMemory<byte> body);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "ParseBody")]
    private static extern FirehoseMessage? ParseBody([UnsafeAccessorType(Parser)] object? _, string type, ReadOnlyMemory<byte> body);

    [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "IsKnownType")]
    private static extern bool IsKnownType([UnsafeAccessorType(Parser)] object? _, string type);
}
