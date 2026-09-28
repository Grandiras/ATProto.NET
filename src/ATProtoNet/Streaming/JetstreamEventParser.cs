using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Serialization;

namespace ATProtoNet.Streaming;

/// <summary>
/// Parses Jetstream JSON frames into typed <see cref="JetstreamEvent"/> objects,
/// on either wire protocol.
/// </summary>
/// <remarks>
/// The parser is forward-tolerant: unknown event kinds, unknown fields, and malformed
/// frames yield an empty result rather than throwing, so consumers keep working when the
/// Jetstream protocol evolves.
/// </remarks>
public static class JetstreamEventParser
{
    private const string V2TypePrefix = "network.bsky.jetstream.subscribeEvents#";

    /// <summary>Parse a single Jetstream frame on the given wire protocol.</summary>
    /// <param name="json">The UTF-8 JSON payload of one WebSocket message. It is read in place,
    /// and the result keeps no reference to it.</param>
    /// <param name="protocol">The wire protocol the frame was received on.</param>
    /// <returns>
    /// The frame's event, advisory notice, or terminal error. All three are null when the
    /// frame is malformed or of a kind this version does not understand — skip it.
    /// </returns>
    public static JetstreamFrame ParseFrame(ReadOnlyMemory<byte> json, JetstreamProtocol protocol)
        => Parse(json, protocol, out _);

    // Parses a frame, and says why when it yields nothing.
    internal static JetstreamFrame Parse(ReadOnlyMemory<byte> json, JetstreamProtocol protocol, out StreamDropReason? dropped)
    {
        dropped = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var frame = protocol == JetstreamProtocol.V2
                ? ParseV2Frame(doc.RootElement, out dropped)
                : new JetstreamFrame(ParseV1Event(doc.RootElement, out dropped), null, null);

            if (frame == default)
                dropped ??= StreamDropReason.Malformed;
            return frame;
        }
        catch (JsonException)
        {
            dropped = StreamDropReason.Malformed;
            return default;
        }
    }

    private static JetstreamFrame ParseV2Frame(JsonElement root, out StreamDropReason? dropped)
    {
        dropped = null;
        if (root.ValueKind != JsonValueKind.Object)
            return default;

        // Every v2 frame is a self-describing envelope: a "message" wrapping one lexicon
        // message under "payload", or a terminal "error".
        var envelope = root.GetStringOrNull("$type"u8);

        if (envelope == "error")
        {
            var name = root.GetStringOrNull("error"u8);
            return name is null
                ? default
                : new JetstreamFrame(null, null, new EventStreamError(name, root.GetStringOrNull("message"u8)));
        }

        if (envelope != "message")
        {
            // Unknown envelope — tolerate for forward compatibility
            dropped = StreamDropReason.UnknownType;
            return default;
        }

        if (!root.TryGetProperty("payload"u8, out var payload) || payload.ValueKind != JsonValueKind.Object)
            return default;

        var type = payload.GetStringOrNull("$type"u8);
        if (type is null)
            return default;

        // Dispatch on the fragment name, so an unqualified "#commit" works too.
        var kind = type.StartsWith(V2TypePrefix, StringComparison.Ordinal)
            ? type[V2TypePrefix.Length..]
            : type.TrimStart('#');

        if (kind == "info")
        {
            var name = payload.GetStringOrNull("name"u8);
            return name is null
                ? default
                : new JetstreamFrame(null, new JetstreamInfo
                {
                    Name = name,
                    Message = payload.GetStringOrNull("message"u8),
                }, null);
        }

        return new JetstreamFrame(ParseV2Event(payload, kind, out dropped), null, null);
    }

    private static JetstreamEvent? ParseV2Event(JsonElement payload, string kind, out StreamDropReason? dropped)
    {
        dropped = kind is "commit" or "identity" or "account" or "sync" ? null : StreamDropReason.UnknownType;
        if (dropped is not null || JetstreamEvents.ParseDid(payload) is not { } did)
            return null;
        if (!TryParseTime(payload, out var time))
            return null;

        var timeUs = (time - DateTime.UnixEpoch).Ticks / 10;
        var cursor = payload.GetInt64OrNull("seq"u8);

        return kind switch
        {
            // v2 flattens the commit fields into the payload; the nested shape is v1's.
            "commit" => ParseCommit(payload, did, timeUs, cursor),
            "identity" => JetstreamEvents.Identity(Nested(payload, "identity"u8), did, timeUs, cursor),
            "account" => Nested(payload, "account"u8) is { } account
                ? JetstreamEvents.Account(account, did, timeUs, cursor)
                : null,
            _ => Nested(payload, "sync"u8) is { } sync
                ? JetstreamEvents.Sync(sync, did, timeUs, cursor, fallbackRev: null)
                : null,
        };
    }

    private static JetstreamEvent? ParseV1Event(JsonElement root, out StreamDropReason? dropped)
    {
        dropped = null;
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (JetstreamEvents.ParseDid(root) is not { } did)
            return null;
        if (root.GetInt64OrNull("time_us"u8) is not { } timeUs)
            return null;

        // A v2 host serving the v1 wire adds its sequence number as "cursor"; a legacy host omits it.
        var cursor = root.GetInt64OrNull("cursor"u8);

        switch (root.GetStringOrNull("kind"u8))
        {
            case "commit":
                return Nested(root, "commit"u8) is { } commit ? ParseCommit(commit, did, timeUs, cursor) : null;
            case "identity":
                return JetstreamEvents.Identity(Nested(root, "identity"u8), did, timeUs, cursor);
            case "account":
                return Nested(root, "account"u8) is { } account ? JetstreamEvents.Account(account, did, timeUs, cursor) : null;
            case null:
                return null;
            default:
                dropped = StreamDropReason.UnknownType; // Unknown kind — tolerate for forward compatibility
                return null;
        }
    }

    // Build a commit event from the element carrying the commit fields — the nested commit object on v1,
    // the whole payload on v2. Both wires name those fields identically, so only the enclosing element
    // differs.
    private static JetstreamCommitEvent? ParseCommit(JsonElement commit, Did did, long timeUs, long? cursor)
    {
        // A commit whose path does not parse names no record a consumer could act on.
        if (commit.GetIdentifierOrNull<Nsid>("collection"u8) is not { } collection)
            return null;
        if (commit.GetIdentifierOrNull<RecordKey>("rkey"u8) is not { } rkey)
            return null;

        // Unknown or missing operation — tolerate for forward compatibility.
        if (!commit.TryGetProperty("operation"u8, out var op) || op.ValueKind != JsonValueKind.String)
            return null;

        RepoOpAction operation;
        if (op.ValueEquals("create"u8))
            operation = RepoOpAction.Create;
        else if (op.ValueEquals("update"u8))
            operation = RepoOpAction.Update;
        else if (op.ValueEquals("delete"u8))
            operation = RepoOpAction.Delete;
        else
            return null;

        // An unparseable CID is dropped rather than the event: the record data is still usable.
        var cid = commit.GetIdentifierOrNull<Cid>("cid"u8);

        return JetstreamEvents.Commit(
            did, timeUs, cursor, collection, rkey, operation,
            JetstreamEvents.ParseTid(commit, "rev"u8),
            cid,
            commit.TryGetProperty("record"u8, out var record) && record.ValueKind == JsonValueKind.Object
                ? record.Clone()
                : null);
    }

    // Reads a v2 frame's time as UTC. The atproto form Jetstream sends is read directly, where that reads
    // it exactly as DateTimeOffset.TryParse would; anything else is left to the framework's parser.
    private static bool TryParseTime(JsonElement payload, out DateTime utc)
    {
        utc = default;
        if (!payload.TryGetProperty("time"u8, out var time) || time.ValueKind != JsonValueKind.String)
            return false;

        var raw = JsonMarshal.GetRawUtf8Value(time);
        if (raw.IndexOf((byte)'\\') < 0 && AtDatetime.TryParseUtc(raw[1..^1], out utc))
            return true;

        if (!DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
            return false;

        utc = parsed.UtcDateTime;
        return true;
    }

    private static JsonElement? Nested(JsonElement element, ReadOnlySpan<byte> name)
        => element.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : null;
}

// Builds JetstreamEvents the same way whether they come from the live wire or an archive segment. An
// identity, account or sync event's fields are read from the same object in both: the live frame nests
// the subscribeRepos message, and a segment row stores it as DAG-CBOR.
internal static class JetstreamEvents
{
    public static JetstreamCommitEvent Commit(
        Did did, long timeUs, long? cursor, Nsid collection, RecordKey rkey, RepoOpAction operation,
        Tid? rev, Cid? cid, JsonElement? record) => new()
    {
        Did = did,
        TimeUs = timeUs,
        Cursor = cursor,
        Collection = collection,
        Rkey = rkey,
        Operation = operation,
        Rev = rev,
        Cid = cid,
        Record = record,
    };

    // An identity event; fields may be missing, since only the DID is required.
    public static JetstreamIdentityEvent Identity(JsonElement? fields, Did did, long timeUs, long? cursor) => new()
    {
        Did = did,
        TimeUs = timeUs,
        Cursor = cursor,
        // A handle that does not parse is dropped rather than the event: the DID is still what a
        // consumer needs to re-resolve the identity.
        Handle = fields is { } identity ? identity.GetIdentifierOrNull<Handle>("handle"u8) : null,
        Seq = fields is { } withSeq ? withSeq.GetInt64OrNull("seq"u8) : null,
        Time = fields is { } withTime ? ParseDatetime(withTime, "time"u8) : null,
    };

    // An account event, or null when fields lacks the required active.
    public static JetstreamAccountEvent? Account(JsonElement fields, Did did, long timeUs, long? cursor)
    {
        if (!fields.TryGetProperty("active"u8, out var active)
            || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;

        return new JetstreamAccountEvent
        {
            Did = did,
            TimeUs = timeUs,
            Cursor = cursor,
            Active = active.GetBoolean(),
            Status = fields.GetStringOrNull("status"u8),
            Seq = fields.GetInt64OrNull("seq"u8),
            Time = ParseDatetime(fields, "time"u8),
        };
    }

    // A sync event; fallbackRev is used when the fields carry no rev.
    public static JetstreamSyncEvent Sync(JsonElement? fields, Did did, long timeUs, long? cursor, string? fallbackRev)
    {
        byte[]? blocks = null;
        // Lexicon bytes arrive in the AT Protocol JSON data model as { "$bytes": "<base64>" }.
        if (fields is { } sync
            && sync.TryGetProperty("blocks"u8, out var blocksProp)
            && blocksProp.ValueKind == JsonValueKind.Object
            && blocksProp.GetStringOrNull("$bytes"u8) is { } base64)
        {
            try
            {
                blocks = LexBase64.Decode(base64);
            }
            catch (FormatException)
            {
                // Tolerate an undecodable CAR — the DID and rev are still actionable
            }
        }

        return new JetstreamSyncEvent
        {
            Did = did,
            TimeUs = timeUs,
            Cursor = cursor,
            Rev = Tid.TryParse((fields is { } withRev ? withRev.GetStringOrNull("rev"u8) : null) ?? fallbackRev, out var rev)
                ? rev
                : null,
            Blocks = blocks,
            Seq = fields is { } withSeq ? withSeq.GetInt64OrNull("seq"u8) : null,
            Time = fields is { } withTime ? ParseDatetime(withTime, "time"u8) : null,
        };
    }

    public static Did? ParseDid(JsonElement element) => element.GetIdentifierOrNull<Did>("did"u8);

    // Optional metadata that does not parse is dropped rather than the event carrying it.
    public static Tid? ParseTid(JsonElement element, ReadOnlySpan<byte> name) => element.GetIdentifierOrNull<Tid>(name);

    // Read leniently, as the JSON converter reads a datetime: the text is kept either way.
    public static AtDatetime? ParseDatetime(JsonElement element, ReadOnlySpan<byte> name)
        => element.GetStringOrNull(name) is { } text ? AtDatetime.FromWire(text) : null;
}
