using System.Text.Json.Nodes;

namespace ATProtoNet.Conformance;

public partial interface IProtocolAdapter
{
    /// <summary>
    /// Reads one <c>com.atproto.sync.subscribeRepos</c> frame as a consumer does, never throwing.
    /// </summary>
    /// <returns>
    /// The outcome as JSON. <c>kind</c> is <c>event</c>, <c>info</c>, <c>error</c> or <c>dropped</c>.
    /// An event has <c>type</c> (the header's <c>t</c>), <c>seq</c> and the body's fields by their
    /// wire names, identifiers and CIDs as strings, <c>ops</c> as <c>{action, path, cid}</c>
    /// objects; an info has <c>name</c> and <c>message</c>; an error frame has <c>error</c> and
    /// <c>message</c>; a dropped frame has <c>reason</c> (<c>malformed</c> or <c>unknownType</c>)
    /// and the <c>seq</c> the cursor advances to, when one can be read.
    /// </returns>
    JsonObject ParseFirehoseFrame(byte[] frame);

    /// <summary>
    /// Verifies a <c>#commit</c> or <c>#sync</c> frame by the Sync 1.1 rules against the stored
    /// state of its repository and applies the outcome.
    /// </summary>
    /// <param name="frame">The frame.</param>
    /// <param name="state">The repository's stored state, or <see langword="null"/> when none is stored.</param>
    /// <param name="signingKey">The <c>did:key</c> the account's DID document publishes as <c>#atproto</c>, or <see langword="null"/> when it publishes no usable key.</param>
    /// <param name="now">The current time.</param>
    SyncVerdict ApplySyncEvent(byte[] frame, SyncState? state, string? signingKey, DateTimeOffset now);
}

/// <summary>A repository's stored sync state.</summary>
/// <param name="Rev">The last revision seen.</param>
/// <param name="Data">The MST root of that revision.</param>
/// <param name="Synchronized">Whether the chain is intact (<see langword="false"/> once desynchronized).</param>
public sealed record SyncState(string? Rev, string? Data, bool Synchronized = true);

/// <summary>The Sync 1.1 outcomes.</summary>
public enum SyncOutcome
{
    /// <summary>Verified and chained.</summary>
    Valid,
    /// <summary>Fails a check; the state is unchanged.</summary>
    Invalid,
    /// <summary>Not after the stored revision; the state is unchanged.</summary>
    Stale,
    /// <summary>Verified but does not chain; the state is marked desynchronized.</summary>
    Desynchronized,
}

/// <summary>The outcome of one Sync 1.1 event and the state stored after it.</summary>
public sealed record SyncVerdict(SyncOutcome Outcome, SyncState? State);
