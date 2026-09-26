using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Serialization;
using ATProtoNet.Streaming;

namespace ATProtoNet.Tap;

/// <summary>
/// An event a Tap instance delivers, over its <c>/channel</c> WebSocket or to a webhook: a
/// <see cref="TapRecordEvent"/> or a <see cref="TapIdentityEvent"/>.
/// </summary>
/// <remarks>
/// <para>Tap verifies the firehose, backfills repositories and filters collections, and sends each
/// change as JSON: <c>{"id": 1, "type": "record", "record": {…}}</c> or
/// <c>{"id": 2, "type": "identity", "identity": {…}}</c>. Delivery is at least once: acknowledge
/// each event once it is processed (<see cref="TapChannel.AckAsync(TapEvent, CancellationToken)"/>),
/// or answer the webhook with a 2xx, and Tap will not send it again.</para>
/// <para>See: https://github.com/bluesky-social/indigo/tree/main/cmd/tap</para>
/// </remarks>
public abstract class TapEvent
{
    private protected TapEvent(long id) => Id = id;

    /// <summary>The event's id: what an acknowledgement names.</summary>
    public long Id { get; }

    /// <summary>
    /// Parses one Tap event from its JSON.
    /// </summary>
    /// <param name="utf8Json">The event, as Tap sends it.</param>
    /// <returns>The event.</returns>
    /// <exception cref="FormatException">
    /// The JSON is malformed, lacks a field, carries an identifier that does not parse, or is of
    /// a type this SDK version does not model.
    /// </exception>
    public static TapEvent Parse(ReadOnlySpan<byte> utf8Json)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8Json);
            var root = JsonElement.ParseValue(ref reader);
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("A Tap event is a JSON object.");

            var id = root.GetProperty("id").GetInt64();
            return root.GetProperty("type").GetString() switch
            {
                "record" => TapRecordEvent.Read(id, root.GetProperty("record")),
                "identity" => TapIdentityEvent.Read(id, root.GetProperty("identity")),
                var type => throw new FormatException($"Tap event type '{type}' is not supported."),
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
        {
            throw new FormatException($"Not a valid Tap event: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// A record created, updated or deleted, from the live firehose or from a backfill.
/// </summary>
/// <remarks>
/// It implements <see cref="IRecordEvent"/>, as the firehose's and Jetstream's record events do,
/// so indexing code can take all three.
/// </remarks>
public sealed class TapRecordEvent : TapEvent, IRecordEvent
{
    private AtUri? _uri;

    private TapRecordEvent(long id) : base(id)
    {
    }

    /// <inheritdoc/>
    public required Did Did { get; init; }

    /// <summary>The revision of the commit that changed the record.</summary>
    public required Tid Rev { get; init; }

    /// <inheritdoc/>
    Tid? IRecordEvent.Rev => Rev;

    /// <inheritdoc/>
    public required Nsid Collection { get; init; }

    /// <inheritdoc/>
    public required RecordKey Rkey { get; init; }

    /// <inheritdoc/>
    public required RepoOpAction Operation { get; init; }

    /// <inheritdoc/>
    public Cid? Cid { get; init; }

    /// <inheritdoc/>
    public JsonElement? Record { get; init; }

    /// <summary>
    /// Whether the event came from the live firehose (<see langword="true"/>) or from backfilling
    /// or resynchronizing the repository (<see langword="false"/>).
    /// </summary>
    public bool Live { get; init; }

    /// <inheritdoc/>
    public AtUri Uri => _uri ??= AtUri.Create(AtIdentifier.FromDid(Did), Collection, Rkey);

    /// <inheritdoc/>
    public T? GetRecord<T>() where T : class
        => Record is { } record ? record.Deserialize<T>(AtProtoJsonDefaults.Options) : null;

    internal static TapRecordEvent Read(long id, JsonElement record) => new(id)
    {
        Did = Did.Parse(record.GetProperty("did").GetString()!),
        Rev = Tid.Parse(record.GetProperty("rev").GetString()!),
        Collection = Nsid.Parse(record.GetProperty("collection").GetString()!),
        Rkey = RecordKey.Parse(record.GetProperty("rkey").GetString()!),
        Operation = record.GetProperty("action").GetString() switch
        {
            "create" => RepoOpAction.Create,
            "update" => RepoOpAction.Update,
            "delete" => RepoOpAction.Delete,
            var action => throw new FormatException($"Tap record action '{action}' is not supported."),
        },
        Cid = record.TryGetProperty("cid", out var cid) && cid.ValueKind == JsonValueKind.String ? Cid.Parse(cid.GetString()!) : null,
        Record = record.TryGetProperty("record", out var value) && value.ValueKind == JsonValueKind.Object ? value : null,
        Live = record.GetProperty("live").GetBoolean(),
    };
}

/// <summary>
/// An account's identity or hosting status changed: its handle, or whether it is active.
/// </summary>
public sealed class TapIdentityEvent : TapEvent
{
    private TapIdentityEvent(long id) : base(id)
    {
    }

    /// <summary>The account.</summary>
    public required Did Did { get; init; }

    /// <summary>The account's handle, or null when Tap reported none.</summary>
    public Handle? Handle { get; init; }

    /// <summary>Whether the account is active.</summary>
    public bool IsActive { get; init; }

    /// <summary>
    /// The account's hosting status: <c>active</c>, <c>takendown</c>, <c>suspended</c>,
    /// <c>deactivated</c> or <c>deleted</c> (<see cref="TapRepoStatus"/>).
    /// </summary>
    public required string Status { get; init; }

    internal static TapIdentityEvent Read(long id, JsonElement identity) => new(id)
    {
        Did = Did.Parse(identity.GetProperty("did").GetString()!),
        Handle = identity.TryGetProperty("handle", out var handle) && handle.GetString() is { Length: > 0 } text ? Handle.Parse(text) : null,
        IsActive = identity.GetProperty("is_active").GetBoolean(),
        Status = identity.GetProperty("status").GetString()
            ?? throw new FormatException("A Tap identity event carries no status."),
    };
}

/// <summary>The account hosting statuses a <see cref="TapIdentityEvent"/> reports.</summary>
public static class TapRepoStatus
{
    /// <summary>The account is active.</summary>
    public const string Active = "active";

    /// <summary>The account was taken down by a service provider.</summary>
    public const string Takendown = "takendown";

    /// <summary>The account is suspended for a time.</summary>
    public const string Suspended = "suspended";

    /// <summary>The account's owner deactivated it.</summary>
    public const string Deactivated = "deactivated";

    /// <summary>The account was deleted.</summary>
    public const string Deleted = "deleted";
}

/// <summary>
/// What Tap knows about a repository it tracks: <c>GET /info/:did</c>.
/// </summary>
public sealed class TapRepoInfo
{
    /// <summary>The repository.</summary>
    public required Did Did { get; init; }

    /// <summary>The account's handle, or null when Tap has not resolved one yet.</summary>
    public Handle? Handle { get; init; }

    /// <summary>
    /// Tap's sync state for the repository, such as <c>pending</c>, <c>resyncing</c>,
    /// <c>active</c>, <c>desynchronized</c> or <c>error</c>.
    /// </summary>
    public required string State { get; init; }

    /// <summary>The last revision Tap processed, or null before its first.</summary>
    public Tid? Rev { get; init; }

    /// <summary>How many of the repository's records Tap tracks.</summary>
    public long Records { get; init; }

    /// <summary>Why the last backfill failed, or null.</summary>
    public string? Error { get; init; }

    /// <summary>How many times the backfill has been retried.</summary>
    public int Retries { get; init; }

    internal static TapRepoInfo Read(JsonElement info) => new()
    {
        Did = Did.Parse(info.GetProperty("did").GetString()!),
        Handle = info.TryGetProperty("handle", out var handle) && handle.GetString() is { Length: > 0 } text ? Handle.Parse(text) : null,
        State = info.GetProperty("state").GetString()!,
        Rev = info.TryGetProperty("rev", out var rev) && rev.GetString() is { Length: > 0 } revText ? Tid.Parse(revText) : null,
        Records = info.GetProperty("records").GetInt64(),
        Error = info.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } message ? message : null,
        Retries = info.TryGetProperty("retries", out var retries) && retries.ValueKind == JsonValueKind.Number ? retries.GetInt32() : 0,
    };
}
