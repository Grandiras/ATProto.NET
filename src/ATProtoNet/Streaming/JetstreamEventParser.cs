using System.Globalization;
using System.Text.Json;
using ATProtoNet.Identity;

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
    /// Parse a single <see cref="JetstreamProtocol.V1"/> Jetstream JSON frame.
    /// </summary>
    /// <param name="json">The UTF-8 JSON payload of one WebSocket message.</param>
    /// <returns>The parsed event, or <c>null</c> if the frame is malformed or of an unknown kind.</returns>
    public static JetstreamEvent? Parse(ReadOnlySpan<byte> json)
        => ParseFrame(json, JetstreamProtocol.V1).Event;

    /// <summary>
    /// Parse a single <see cref="JetstreamProtocol.V1"/> Jetstream JSON frame from a string.
    /// </summary>
    /// <param name="json">The JSON payload of one WebSocket message.</param>
    /// <returns>The parsed event, or <c>null</c> if the frame is malformed or of an unknown kind.</returns>
    public static JetstreamEvent? Parse(string json)
        => ParseFrame(json, JetstreamProtocol.V1).Event;

    /// <summary>
    /// Parse a single Jetstream frame on the given wire protocol.
    /// </summary>
    /// <param name="json">The UTF-8 JSON payload of one WebSocket message.</param>
    /// <param name="protocol">The wire protocol the frame was received on.</param>
    /// <returns>
    /// The frame's event, advisory notice, or terminal error. All three are null when the
    /// frame is malformed or of a kind this version does not understand — skip it.
    /// </returns>
    public static JetstreamFrame ParseFrame(ReadOnlySpan<byte> json, JetstreamProtocol protocol)
    {
        // JsonDocument cannot parse a span without copying it to the heap first. Callers on
        // the hot path (the WebSocket read loop) already hold the frame as an array and
        // should use the ReadOnlyMemory overload, which skips this copy.
        return ParseFrame(new ReadOnlyMemory<byte>(json.ToArray()), protocol);
    }

    /// <summary>
    /// Parse a single Jetstream frame on the given wire protocol.
    /// </summary>
    /// <param name="json">The UTF-8 JSON payload of one WebSocket message.</param>
    /// <param name="protocol">The wire protocol the frame was received on.</param>
    /// <returns>
    /// The frame's event, advisory notice, or terminal error. All three are null when the
    /// frame is malformed or of a kind this version does not understand — skip it.
    /// </returns>
    /// <remarks>
    /// Preferred over the <see cref="ReadOnlySpan{T}"/> overload when the payload is already
    /// on the heap: the frame is read in place rather than copied.
    /// </remarks>
    public static JetstreamFrame ParseFrame(ReadOnlyMemory<byte> json, JetstreamProtocol protocol)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseFrameCore(doc.RootElement, protocol);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    /// <summary>
    /// Parse a single Jetstream frame on the given wire protocol, from a string.
    /// </summary>
    /// <param name="json">The JSON payload of one WebSocket message.</param>
    /// <param name="protocol">The wire protocol the frame was received on.</param>
    /// <returns>
    /// The frame's event, advisory notice, or terminal error. All three are null when the
    /// frame is malformed or of a kind this version does not understand — skip it.
    /// </returns>
    public static JetstreamFrame ParseFrame(string json, JetstreamProtocol protocol)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var doc = JsonDocument.Parse(json);
            return ParseFrameCore(doc.RootElement, protocol);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static JetstreamFrame ParseFrameCore(JsonElement root, JetstreamProtocol protocol)
        => protocol == JetstreamProtocol.V2
            ? ParseV2Frame(root)
            : new JetstreamFrame(ParseCore(root), null, null);

    private static JetstreamFrame ParseV2Frame(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return default;

        // Every v2 frame is a self-describing envelope: a "message" wrapping one lexicon
        // message under "payload", or a terminal "error".
        var envelope = GetString(root, "$type");

        if (envelope == "error")
        {
            var name = GetString(root, "error");
            return name is null
                ? default
                : new JetstreamFrame(null, null, new JetstreamStreamError
                {
                    Error = name,
                    Message = GetString(root, "message"),
                });
        }

        if (envelope != "message")
            return default; // Unknown envelope — tolerate for forward compatibility

        if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            return default;

        var type = GetString(payload, "$type");
        if (type is null)
            return default;

        // Dispatch on the fragment name, so an unqualified "#commit" works too.
        var kind = type.StartsWith(V2TypePrefix, StringComparison.Ordinal)
            ? type[V2TypePrefix.Length..]
            : type.TrimStart('#');

        if (kind == "info")
        {
            var name = GetString(payload, "name");
            return name is null
                ? default
                : new JetstreamFrame(null, new JetstreamInfo
                {
                    Name = name,
                    Message = GetString(payload, "message"),
                }, null);
        }

        return new JetstreamFrame(ParseV2Event(payload, kind), null, null);
    }

    private static JetstreamEvent? ParseV2Event(JsonElement payload, string kind)
    {
        if (ParseDid(payload) is not { } did)
            return null;
        if (!payload.TryGetProperty("time", out var timeProp) || timeProp.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(timeProp.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time))
            return null;

        var timeUs = (time.UtcDateTime - DateTime.UnixEpoch).Ticks / 10;
        var cursor = payload.TryGetProperty("seq", out var seqProp) && seqProp.TryGetInt64(out var seq)
            ? seq
            : (long?)null;

        return kind switch
        {
            // v2 flattens the commit fields into the payload; the nested shape is v1's.
            "commit" => ParseCommitFields(payload, did, timeUs, cursor),
            "identity" => ParseIdentity(payload, did, timeUs, cursor),
            "account" => ParseAccount(payload, did, timeUs, cursor),
            "sync" => ParseSync(payload, did, timeUs, cursor),
            _ => null, // Unknown kind — tolerate for forward compatibility
        };
    }

    private static JetstreamEvent? ParseCore(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return null;

        if (ParseDid(root) is not { } did)
            return null;
        if (!root.TryGetProperty("time_us", out var timeProp) || !timeProp.TryGetInt64(out var timeUs))
            return null;
        if (!root.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String)
            return null;

        // A v2 host serving the v1 wire adds its sequence number as "cursor"; a legacy host omits it.
        var cursor = root.TryGetProperty("cursor", out var cursorProp) && cursorProp.TryGetInt64(out var seq)
            ? seq
            : (long?)null;

        return kindProp.GetString() switch
        {
            "commit" => root.TryGetProperty("commit", out var commit) && commit.ValueKind == JsonValueKind.Object
                ? ParseCommitFields(commit, did, timeUs, cursor)
                : null,
            "identity" => ParseIdentity(root, did, timeUs, cursor),
            "account" => ParseAccount(root, did, timeUs, cursor),
            _ => null, // Unknown kind — tolerate for forward compatibility
        };
    }

    /// <summary>
    /// Build a commit event from the element carrying the commit fields — the nested
    /// <c>commit</c> object on v1, the whole payload on v2. Both wires name those fields
    /// identically, so only the enclosing element differs.
    /// </summary>
    private static JetstreamCommitEvent? ParseCommitFields(
        JsonElement commit, Did did, long timeUs, long? cursor)
    {
        if (GetString(commit, "collection") is not { } collection)
            return null;
        if (GetString(commit, "rkey") is not { } rkey)
            return null;

        JetstreamOperation operation;
        switch (GetString(commit, "operation"))
        {
            case "create": operation = JetstreamOperation.Create; break;
            case "update": operation = JetstreamOperation.Update; break;
            case "delete": operation = JetstreamOperation.Delete; break;
            default: return null; // Unknown or missing operation — tolerate for forward compatibility
        }

        Cid? cid = null;
        if (GetString(commit, "cid") is { } cidText)
        {
            try
            {
                cid = Cid.Parse(cidText);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException)
            {
                // Tolerate unparseable CIDs — the record data is still usable
            }
        }

        return new JetstreamCommitEvent
        {
            Did = did,
            TimeUs = timeUs,
            Cursor = cursor,
            Collection = collection,
            RKey = rkey,
            Operation = operation,
            Rev = GetString(commit, "rev"),
            Cid = cid,
            Record = commit.TryGetProperty("record", out var record) && record.ValueKind == JsonValueKind.Object
                ? record.Clone()
                : null,
        };
    }

    private static JetstreamIdentityEvent ParseIdentity(JsonElement root, Did did, long timeUs, long? cursor)
    {
        string? handle = null;
        long? seq = null;
        string? time = null;

        if (root.TryGetProperty("identity", out var identity) && identity.ValueKind == JsonValueKind.Object)
        {
            handle = GetString(identity, "handle");
            if (identity.TryGetProperty("seq", out var seqProp) && seqProp.TryGetInt64(out var seqValue))
                seq = seqValue;
            time = GetString(identity, "time");
        }

        return new JetstreamIdentityEvent
        {
            Did = did,
            TimeUs = timeUs,
            Cursor = cursor,
            Handle = handle,
            Seq = seq,
            Time = time,
        };
    }

    private static JetstreamAccountEvent? ParseAccount(JsonElement root, Did did, long timeUs, long? cursor)
    {
        if (!root.TryGetProperty("account", out var account) || account.ValueKind != JsonValueKind.Object)
            return null;

        if (!account.TryGetProperty("active", out var activeProp)
            || activeProp.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return null;

        return new JetstreamAccountEvent
        {
            Did = did,
            TimeUs = timeUs,
            Cursor = cursor,
            Active = activeProp.GetBoolean(),
            Status = GetString(account, "status"),
            Seq = account.TryGetProperty("seq", out var seqProp) && seqProp.TryGetInt64(out var seqValue)
                ? seqValue
                : null,
            Time = GetString(account, "time"),
        };
    }

    private static JetstreamSyncEvent? ParseSync(JsonElement root, Did did, long timeUs, long? cursor)
    {
        if (!root.TryGetProperty("sync", out var sync) || sync.ValueKind != JsonValueKind.Object)
            return null;

        byte[]? blocks = null;
        // Lexicon "bytes" arrive in the AT Protocol JSON data model as { "$bytes": "<base64>" }.
        if (sync.TryGetProperty("blocks", out var blocksProp)
            && blocksProp.ValueKind == JsonValueKind.Object
            && GetString(blocksProp, "$bytes") is { } base64)
        {
            try
            {
                blocks = Convert.FromBase64String(base64);
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
            Rev = GetString(sync, "rev"),
            Blocks = blocks,
            Seq = sync.TryGetProperty("seq", out var seqProp) && seqProp.TryGetInt64(out var seqValue)
                ? seqValue
                : null,
            Time = GetString(sync, "time"),
        };
    }

    private static Did? ParseDid(JsonElement element)
    {
        if (GetString(element, "did") is not { } text)
            return null;

        try
        {
            return Did.Parse(text);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
}
