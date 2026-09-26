using System.Globalization;
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

    /// <summary>
    /// Parse a single Jetstream frame on the given wire protocol.
    /// </summary>
    /// <param name="json">The UTF-8 JSON payload of one WebSocket message. It is read in place,
    /// and the result keeps no reference to it.</param>
    /// <param name="protocol">The wire protocol the frame was received on.</param>
    /// <returns>
    /// The frame's event, advisory notice, or terminal error. All three are null when the
    /// frame is malformed or of a kind this version does not understand — skip it.
    /// </returns>
    public static JetstreamFrame ParseFrame(ReadOnlyMemory<byte> json, JetstreamProtocol protocol)
        => Parse(json, protocol, out _);

    /// <summary>
    /// Parses a frame, and says why when it yields nothing.
    /// </summary>
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
        var envelope = JetstreamEvents.GetString(root, "$type");

        if (envelope == "error")
        {
            var name = JetstreamEvents.GetString(root, "error");
            return name is null
                ? default
                : new JetstreamFrame(null, null, new EventStreamError(name, JetstreamEvents.GetString(root, "message")));
        }

        if (envelope != "message")
        {
            // Unknown envelope — tolerate for forward compatibility
            dropped = StreamDropReason.UnknownType;
            return default;
        }

        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return default;

        var type = JetstreamEvents.GetString(payload, "$type");
        if (type is null)
            return default;

        // Dispatch on the fragment name, so an unqualified "#commit" works too.
        var kind = type.StartsWith(V2TypePrefix, StringComparison.Ordinal)
            ? type[V2TypePrefix.Length..]
            : type.TrimStart('#');

        if (kind == "info")
        {
            var name = JetstreamEvents.GetString(payload, "name");
            return name is null
                ? default
                : new JetstreamFrame(null, new JetstreamInfo
                {
                    Name = name,
                    Message = JetstreamEvents.GetString(payload, "message"),
                }, null);
        }

        return new JetstreamFrame(ParseV2Event(payload, kind, out dropped), null, null);
    }

    private static JetstreamEvent? ParseV2Event(JsonElement payload, string kind, out StreamDropReason? dropped)
    {
        dropped = kind is "commit" or "identity" or "account" or "sync" ? null : StreamDropReason.UnknownType;
        if (dropped is not null || JetstreamEvents.ParseDid(payload) is not { } did)
            return null;
        if (!payload.TryGetProperty("time", out var timeProp) || timeProp.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(timeProp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
            return null;

        var timeUs = (time.UtcDateTime - DateTime.UnixEpoch).Ticks / 10;
        var cursor = JetstreamEvents.GetInt64(payload, "seq");

        return kind switch
        {
            // v2 flattens the commit fields into the payload; the nested shape is v1's.
            "commit" => ParseCommit(payload, did, timeUs, cursor),
            "identity" => JetstreamEvents.Identity(Nested(payload, "identity"), did, timeUs, cursor),
            "account" => Nested(payload, "account") is { } account
                ? JetstreamEvents.Account(account, did, timeUs, cursor)
                : null,
            _ => Nested(payload, "sync") is { } sync
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
        if (JetstreamEvents.GetInt64(root, "time_us") is not { } timeUs)
            return null;

        // A v2 host serving the v1 wire adds its sequence number as "cursor"; a legacy host omits it.
        var cursor = JetstreamEvents.GetInt64(root, "cursor");

        switch (JetstreamEvents.GetString(root, "kind"))
        {
            case "commit":
                return Nested(root, "commit") is { } commit ? ParseCommit(commit, did, timeUs, cursor) : null;
            case "identity":
                return JetstreamEvents.Identity(Nested(root, "identity"), did, timeUs, cursor);
            case "account":
                return Nested(root, "account") is { } account ? JetstreamEvents.Account(account, did, timeUs, cursor) : null;
            case null:
                return null;
            default:
                dropped = StreamDropReason.UnknownType; // Unknown kind — tolerate for forward compatibility
                return null;
        }
    }

    /// <summary>
    /// Build a commit event from the element carrying the commit fields — the nested
    /// <c>commit</c> object on v1, the whole payload on v2. Both wires name those fields
    /// identically, so only the enclosing element differs.
    /// </summary>
    private static JetstreamCommitEvent? ParseCommit(JsonElement commit, Did did, long timeUs, long? cursor)
    {
        // A commit whose path does not parse names no record a consumer could act on.
        if (!Nsid.TryParse(JetstreamEvents.GetString(commit, "collection"), out var collection))
            return null;
        if (!RecordKey.TryParse(JetstreamEvents.GetString(commit, "rkey"), out var rkey))
            return null;

        RepoOpAction operation;
        switch (JetstreamEvents.GetString(commit, "operation"))
        {
            case "create": operation = RepoOpAction.Create; break;
            case "update": operation = RepoOpAction.Update; break;
            case "delete": operation = RepoOpAction.Delete; break;
            default: return null; // Unknown or missing operation — tolerate for forward compatibility
        }

        // An unparseable CID is dropped rather than the event: the record data is still usable.
        Cid.TryParse(JetstreamEvents.GetString(commit, "cid"), out var cid);

        return JetstreamEvents.Commit(
            did, timeUs, cursor, collection, rkey, operation,
            JetstreamEvents.ParseTid(commit, "rev"),
            cid,
            commit.TryGetProperty("record", out var record) && record.ValueKind == JsonValueKind.Object
                ? record.Clone()
                : null);
    }

    private static JsonElement? Nested(JsonElement element, string name)
        => element.TryGetProperty(name, out var nested) && nested.ValueKind == JsonValueKind.Object
            ? nested
            : null;
}

/// <summary>
/// Builds <see cref="JetstreamEvent"/>s the same way whether they come from the live wire or an
/// archive segment. An identity, account or sync event's fields are read from the same object in
/// both: the live frame nests the <c>subscribeRepos</c> message, and a segment row stores it as
/// DAG-CBOR.
/// </summary>
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

    /// <summary>An identity event; <paramref name="fields"/> may be missing, since only the DID is required.</summary>
    public static JetstreamIdentityEvent Identity(JsonElement? fields, Did did, long timeUs, long? cursor) => new()
    {
        Did = did,
        TimeUs = timeUs,
        Cursor = cursor,
        // A handle that does not parse is dropped rather than the event: the DID is still what a
        // consumer needs to re-resolve the identity.
        Handle = fields is { } identity && Handle.TryParse(GetString(identity, "handle"), out var handle) ? handle : null,
        Seq = fields is { } withSeq ? GetInt64(withSeq, "seq") : null,
        Time = fields is { } withTime ? ParseDatetime(withTime, "time") : null,
    };

    /// <summary>An account event, or null when <paramref name="fields"/> lacks the required <c>active</c>.</summary>
    public static JetstreamAccountEvent? Account(JsonElement fields, Did did, long timeUs, long? cursor)
    {
        if (!fields.TryGetProperty("active", out var active)
            || active.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;

        return new JetstreamAccountEvent
        {
            Did = did,
            TimeUs = timeUs,
            Cursor = cursor,
            Active = active.GetBoolean(),
            Status = GetString(fields, "status"),
            Seq = GetInt64(fields, "seq"),
            Time = ParseDatetime(fields, "time"),
        };
    }

    /// <summary>A sync event; <paramref name="fallbackRev"/> is used when the fields carry no <c>rev</c>.</summary>
    public static JetstreamSyncEvent Sync(JsonElement? fields, Did did, long timeUs, long? cursor, string? fallbackRev)
    {
        byte[]? blocks = null;
        // Lexicon bytes arrive in the AT Protocol JSON data model as { "$bytes": "<base64>" }.
        if (fields is { } sync
            && sync.TryGetProperty("blocks", out var blocksProp)
            && blocksProp.ValueKind == JsonValueKind.Object
            && GetString(blocksProp, "$bytes") is { } base64)
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
            Rev = Tid.TryParse((fields is { } withRev ? GetString(withRev, "rev") : null) ?? fallbackRev, out var rev)
                ? rev
                : null,
            Blocks = blocks,
            Seq = fields is { } withSeq ? GetInt64(withSeq, "seq") : null,
            Time = fields is { } withTime ? ParseDatetime(withTime, "time") : null,
        };
    }

    public static Did? ParseDid(JsonElement element)
        => Did.TryParse(GetString(element, "did"), out var did) ? did : null;

    public static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;

    public static long? GetInt64(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetInt64(out var number)
            ? number
            : null;

    // Optional metadata that does not parse is dropped rather than the event carrying it.
    public static Tid? ParseTid(JsonElement element, string name)
        => Tid.TryParse(GetString(element, name), out var tid) ? tid : null;

    // Read leniently, as the JSON converter reads a datetime: the text is kept either way.
    public static AtDatetime? ParseDatetime(JsonElement element, string name)
        => GetString(element, name) is { } text ? AtDatetime.FromWire(text) : null;
}
