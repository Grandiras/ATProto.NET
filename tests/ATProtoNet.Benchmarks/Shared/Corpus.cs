using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ATProtoNet.Benchmarks;

/// <summary>
/// Deterministic inputs that name no SDK type, so every implementation benchmarks the same
/// bytes: records, a repository's keys and values, a timeline response, Jetstream frames.
/// </summary>
public static class Corpus
{
    public const int RepoSize = 2000;
    public const int TimelinePosts = 100;
    public const int JetstreamFrameCount = 200;

    private static readonly string[] Collections = ["app.bsky.feed.post", "app.bsky.feed.like", "app.bsky.feed.repost"];

    public static string Did(int n) => $"did:plc:{Base32(Hash(n))[..24]}";

    /// <summary>A CIDv1 (dag-cbor, sha-256) over a hash of <paramref name="seed"/>, as binary.</summary>
    public static byte[] CidBytes(int seed) => [0x01, 0x71, 0x12, 0x20, .. Hash(seed)];

    public static string Cid(int seed) => "b" + Base32(CidBytes(seed));

    /// <summary>A record-key shaped like a TID, increasing with <paramref name="n"/>.</summary>
    public static string Tid(int n)
    {
        var value = 1_700_000_000_000_000L + n * 977L;
        var chars = new char[13];
        for (var i = 12; i >= 0; i--, value >>= 5)
            chars[i] = "234567abcdefghijklmnopqrstuvwxyz"[(int)(value & 31)];
        return new string(chars);
    }

    /// <summary>The repository paths and record CIDs of a mixed repository of <see cref="RepoSize"/> records.</summary>
    public static (string Path, byte[] Cid)[] RepoEntries() =>
        [.. Enumerable.Range(0, RepoSize).Select(i => ($"{Collections[i % Collections.Length]}/{Tid(i)}", CidBytes(i)))];

    /// <summary>Post records as the data model's JSON form (<c>$link</c>, <c>$bytes</c>), one per repository post.</summary>
    public static string PostRecordJson(int n) => Write(w => WritePostRecord(w, n, withBlob: true));

    /// <summary>A document of 64 records, for encoding and decoding as DRISL.</summary>
    public static string RecordBatchJson() => Write(w =>
    {
        w.WriteStartObject();
        w.WriteString("$type", "bench.records");
        w.WriteStartArray("records");
        for (var i = 0; i < 64; i++)
            WritePostRecord(w, i, withBlob: true);
        w.WriteEndArray();
        w.WriteEndObject();
    });

    /// <summary>A <c>getTimeline</c> response of <see cref="TimelinePosts"/> posts: embeds, replies, reposts.</summary>
    public static byte[] TimelineJson() => WriteUtf8(w =>
    {
        w.WriteStartObject();
        w.WriteString("cursor", "2026-10-01T00:00:00.000Z");
        w.WriteStartArray("feed");
        for (var i = 0; i < TimelinePosts; i++)
            WriteFeedItem(w, i);
        w.WriteEndArray();
        w.WriteEndObject();
    });

    public static byte[] ProfileJson() => WriteUtf8(w =>
    {
        w.WriteStartObject();
        w.WriteString("did", Did(1));
        w.WriteString("handle", "user1.example.com");
        w.WriteString("displayName", "User One");
        w.WriteString("description", "A deterministic profile used by the benchmarks.");
        w.WriteString("avatar", "https://cdn.example.com/avatar/1.jpg");
        w.WriteNumber("followersCount", 1234);
        w.WriteNumber("followsCount", 321);
        w.WriteNumber("postsCount", 4567);
        w.WriteString("indexedAt", "2026-10-01T00:00:00.000Z");
        w.WriteString("createdAt", "2024-01-01T00:00:00.000Z");
        w.WriteEndObject();
    });

    /// <summary>Jetstream (v1) frames: mostly commits (creates of posts and likes, deletes), some identity and account events.</summary>
    public static byte[][] JetstreamFrames() =>
        [.. Enumerable.Range(0, JetstreamFrameCount).Select(i => WriteUtf8(w => WriteJetstreamFrame(w, i)))];

    public static string DidDocumentJson(string did, string handle, string pds, string? signingKeyMultibase = null) => Write(w =>
    {
        w.WriteStartObject();
        w.WriteString("id", did);
        w.WriteStartArray("alsoKnownAs");
        w.WriteStringValue($"at://{handle}");
        w.WriteEndArray();
        w.WriteStartArray("verificationMethod");
        if (signingKeyMultibase is not null)
        {
            w.WriteStartObject();
            w.WriteString("id", $"{did}#atproto");
            w.WriteString("type", "Multikey");
            w.WriteString("controller", did);
            w.WriteString("publicKeyMultibase", signingKeyMultibase);
            w.WriteEndObject();
        }
        w.WriteEndArray();
        w.WriteStartArray("service");
        w.WriteStartObject();
        w.WriteString("id", "#atproto_pds");
        w.WriteString("type", "AtprotoPersonalDataServer");
        w.WriteString("serviceEndpoint", pds);
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
    });

    private static void WritePostRecord(Utf8JsonWriter w, int n, bool withBlob)
    {
        var text = $"Post {n}: the quick brown fox jumps over the lazy dog, again and again.";
        w.WriteStartObject();
        w.WriteString("$type", "app.bsky.feed.post");
        w.WriteString("text", text);
        w.WriteString("createdAt", Timestamp(n));
        w.WriteStartArray("langs");
        w.WriteStringValue("en");
        w.WriteEndArray();
        w.WriteStartArray("facets");
        w.WriteStartObject();
        w.WriteStartObject("index");
        w.WriteNumber("byteStart", 0);
        w.WriteNumber("byteEnd", 6);
        w.WriteEndObject();
        w.WriteStartArray("features");
        w.WriteStartObject();
        w.WriteString("$type", "app.bsky.richtext.facet#link");
        w.WriteString("uri", $"https://example.com/p/{n}");
        w.WriteEndObject();
        w.WriteEndArray();
        w.WriteEndObject();
        w.WriteEndArray();
        if (n % 4 == 0)
        {
            w.WriteStartObject("reply");
            WriteStrongRef(w, "root", n + 1);
            WriteStrongRef(w, "parent", n + 2);
            w.WriteEndObject();
        }
        if (withBlob && n % 3 == 0)
        {
            w.WriteStartObject("embed");
            w.WriteString("$type", "app.bsky.embed.images");
            w.WriteStartArray("images");
            w.WriteStartObject();
            w.WriteString("alt", $"Image {n}");
            w.WriteStartObject("image");
            w.WriteString("$type", "blob");
            w.WriteStartObject("ref");
            w.WriteString("$link", Cid(n + 10_000));
            w.WriteEndObject();
            w.WriteString("mimeType", "image/jpeg");
            w.WriteNumber("size", 100_000 + n);
            w.WriteEndObject();
            w.WriteStartObject("aspectRatio");
            w.WriteNumber("width", 1600);
            w.WriteNumber("height", 900);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    private static void WriteStrongRef(Utf8JsonWriter w, string name, int n)
    {
        w.WriteStartObject(name);
        w.WriteString("uri", $"at://{Did(n)}/app.bsky.feed.post/{Tid(n)}");
        w.WriteString("cid", Cid(n));
        w.WriteEndObject();
    }

    private static void WriteAuthor(Utf8JsonWriter w, string name, int n)
    {
        w.WriteStartObject(name);
        w.WriteString("did", Did(n % 20));
        w.WriteString("handle", $"user{n % 20}.example.com");
        w.WriteString("displayName", $"User {n % 20}");
        w.WriteString("avatar", $"https://cdn.example.com/avatar/{n % 20}.jpg");
        w.WriteStartObject("viewer");
        w.WriteBoolean("muted", false);
        w.WriteBoolean("blockedBy", false);
        w.WriteEndObject();
        w.WriteStartArray("labels");
        w.WriteEndArray();
        w.WriteString("createdAt", "2024-01-01T00:00:00.000Z");
        w.WriteEndObject();
    }

    private static void WriteFeedItem(Utf8JsonWriter w, int n)
    {
        w.WriteStartObject();
        w.WriteStartObject("post");
        w.WriteString("uri", $"at://{Did(n % 20)}/app.bsky.feed.post/{Tid(n)}");
        w.WriteString("cid", Cid(n));
        WriteAuthor(w, "author", n);
        w.WritePropertyName("record");
        WritePostRecord(w, n, withBlob: false);
        if (n % 3 == 0)
        {
            w.WriteStartObject("embed");
            w.WriteString("$type", "app.bsky.embed.images#view");
            w.WriteStartArray("images");
            w.WriteStartObject();
            w.WriteString("thumb", $"https://cdn.example.com/thumb/{n}.jpg");
            w.WriteString("fullsize", $"https://cdn.example.com/full/{n}.jpg");
            w.WriteString("alt", $"Image {n}");
            w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();
        }
        w.WriteNumber("replyCount", n % 7);
        w.WriteNumber("repostCount", n % 11);
        w.WriteNumber("likeCount", n * 3 % 101);
        w.WriteNumber("quoteCount", n % 5);
        w.WriteString("indexedAt", Timestamp(n));
        w.WriteStartObject("viewer");
        w.WriteEndObject();
        w.WriteStartArray("labels");
        w.WriteEndArray();
        w.WriteEndObject();
        if (n % 10 == 5)
        {
            w.WriteStartObject("reason");
            w.WriteString("$type", "app.bsky.feed.defs#reasonRepost");
            WriteAuthor(w, "by", n + 1);
            w.WriteString("indexedAt", Timestamp(n));
            w.WriteEndObject();
        }
        w.WriteEndObject();
    }

    private static void WriteJetstreamFrame(Utf8JsonWriter w, int n)
    {
        var did = Did(n % 50);
        w.WriteStartObject();
        w.WriteString("did", did);
        w.WriteNumber("time_us", 1_790_000_000_000_000L + n * 1_237L);
        switch (n % 10)
        {
            case 8:
                w.WriteString("kind", "identity");
                w.WriteStartObject("identity");
                w.WriteString("did", did);
                w.WriteString("handle", $"user{n % 50}.example.com");
                w.WriteNumber("seq", 1_400_000_000 + n);
                w.WriteString("time", Timestamp(n));
                w.WriteEndObject();
                break;
            case 9:
                w.WriteString("kind", "account");
                w.WriteStartObject("account");
                w.WriteBoolean("active", true);
                w.WriteString("did", did);
                w.WriteNumber("seq", 1_400_000_000 + n);
                w.WriteString("time", Timestamp(n));
                w.WriteEndObject();
                break;
            default:
                w.WriteString("kind", "commit");
                w.WriteStartObject("commit");
                w.WriteString("rev", Tid(n));
                w.WriteString("operation", n % 10 == 7 ? "delete" : "create");
                w.WriteString("collection", n % 2 == 0 ? "app.bsky.feed.post" : "app.bsky.feed.like");
                w.WriteString("rkey", Tid(n + 1));
                if (n % 10 != 7)
                {
                    w.WritePropertyName("record");
                    if (n % 2 == 0)
                        WritePostRecord(w, n, withBlob: true);
                    else
                    {
                        w.WriteStartObject();
                        w.WriteString("$type", "app.bsky.feed.like");
                        w.WriteString("createdAt", Timestamp(n));
                        WriteStrongRef(w, "subject", n);
                        w.WriteEndObject();
                    }
                    w.WriteString("cid", Cid(n));
                }
                w.WriteEndObject();
                break;
        }
        w.WriteEndObject();
    }

    private static string Timestamp(int n) =>
        new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(n * 31L).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");

    private static byte[] Hash(int seed) => SHA256.HashData(Encoding.ASCII.GetBytes($"atproto-net-bench-{seed}"));

    private static string Base32(ReadOnlySpan<byte> data)
    {
        var sb = new StringBuilder();
        for (int buffer = 0, bits = 0, i = 0; i < data.Length || bits > 0; )
        {
            if (bits < 5 && i < data.Length) (buffer, bits) = ((buffer << 8) | data[i++], bits + 8);
            else if (bits < 5) (buffer, bits) = (buffer << (5 - bits), 5);
            sb.Append("abcdefghijklmnopqrstuvwxyz234567"[(buffer >> (bits - 5)) & 31]);
            bits -= 5;
            buffer &= (1 << bits) - 1;
        }
        return sb.ToString();
    }

    private static byte[] WriteUtf8(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            write(writer);
        return stream.ToArray();
    }

    private static string Write(Action<Utf8JsonWriter> write) => Encoding.UTF8.GetString(WriteUtf8(write));
}
