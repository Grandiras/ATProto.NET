using ATProtoNet.Crypto;

namespace ATProtoNet.Pds;

/// <summary>
/// The DID method a PDS mints identities with.
/// </summary>
public enum PdsDidMethod
{
    /// <summary>
    /// <c>did:plc</c> — the network's primary method. Identities are registered by submitting a
    /// signed genesis operation to a PLC directory.
    /// </summary>
    Plc,

    /// <summary>
    /// <c>did:web</c> — the DID is the handle's domain and resolves from
    /// <c>https://&lt;handle&gt;/.well-known/did.json</c>, served by this PDS. Requires each
    /// handle to be a domain pointing at the PDS, but needs no external directory.
    /// </summary>
    Web,
}

/// <summary>
/// Configuration options for the PDS host.
/// </summary>
public sealed class PdsOptions
{
    /// <summary>
    /// The public hostname of this PDS (e.g. "pds.example.com").
    /// Used to form AT URIs and in DID documents.
    /// </summary>
    public string Hostname { get; set; } = "localhost";

    /// <summary>
    /// The public URL of this PDS (e.g. "https://pds.example.com").
    /// Defaults to https://{Hostname}.
    /// </summary>
    public string? PublicUrl { get; set; }

    /// <summary>
    /// Whether to allow open registration (any user can create an account).
    /// When false, invite codes are required.
    /// </summary>
    public bool OpenRegistration { get; set; }

    /// <summary>
    /// Maximum blob size in bytes. Default: 5 MB.
    /// </summary>
    public long MaxBlobSize { get; set; } = 5 * 1024 * 1024;

    /// <summary>
    /// Supported DID methods for account creation. Default: ["plc"].
    /// </summary>
    public List<string> AvailableUserDomains { get; set; } = [];

    /// <summary>
    /// Contact email for the PDS operator.
    /// </summary>
    public string? ContactEmail { get; set; }

    // ──────────────────────────────────────────────────────────
    //  Federation
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// The DID method used to mint identities for new accounts. Default: <see cref="PdsDidMethod.Plc"/>.
    /// </summary>
    public PdsDidMethod DidMethod { get; set; } = PdsDidMethod.Plc;

    /// <summary>
    /// The PLC directory to register <c>did:plc</c> identities with.
    /// Default: <c>https://plc.directory</c>.
    /// </summary>
    public string PlcDirectoryUrl { get; set; } = Identity.PlcClient.DefaultDirectoryUrl;

    /// <summary>
    /// Whether to actually submit genesis operations to <see cref="PlcDirectoryUrl"/>.
    /// <para>
    /// Default <c>false</c>: DIDs are still derived the real way — from the hash of a signed
    /// genesis operation, so they are well-formed and self-consistent — but nothing is published.
    /// Turn this on for a PDS that must be resolvable by the wider network. Keeping it off by
    /// default means neither the test suite nor a local development host writes to a public,
    /// append-only directory as a side effect of creating an account.
    /// </para>
    /// </summary>
    public bool RegisterDidsWithPlc { get; set; }

    /// <summary>
    /// The curve used for newly generated repo signing and rotation keys.
    /// Default: <see cref="KeyCurve.P256"/>, which every platform supports; <see cref="KeyCurve.K256"/>
    /// matches the reference implementation but needs OpenSSL.
    /// </summary>
    public KeyCurve SigningKeyCurve { get; set; } = KeyCurve.P256;

    /// <summary>
    /// Relay hosts to notify with <c>com.atproto.sync.requestCrawl</c> so they begin crawling
    /// this PDS. Entries may be hostnames or full URLs, e.g. <c>bsky.network</c>.
    /// </summary>
    public List<string> RelayHosts { get; set; } = [];

    /// <summary>
    /// How many firehose events to retain for cursor-based replay by reconnecting relays.
    /// Default: <see cref="PdsSequencer.DefaultBacklogCapacity"/>.
    /// </summary>
    public int FirehoseBacklogCapacity { get; set; } = PdsSequencer.DefaultBacklogCapacity;

    /// <summary>
    /// Maximum size of the CAR payload inlined into a <c>#commit</c> firehose event. Larger
    /// commits are published with <c>tooBig</c> set and no blocks, leaving consumers to fetch
    /// the repo through <c>com.atproto.sync.getRepo</c>. Default: 1 MiB.
    /// </summary>
    public long MaxFirehoseFrameBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// Whether <c>MapAtProtoPds()</c> serves <c>/.well-known/did.json</c> for <c>did:web</c>
    /// accounts whose DID matches the request host. Default: <c>true</c>.
    /// </summary>
    public bool ServeWellKnownDidDocument { get; set; } = true;

    /// <summary>
    /// Whether <c>MapAtProtoPds()</c> serves <c>/.well-known/atproto-did</c>, resolving the
    /// request host as a handle. Default: <c>true</c>.
    /// </summary>
    public bool ServeWellKnownHandle { get; set; } = true;

    /// <summary>
    /// Base64-encoded HMAC-SHA256 key used to sign and validate session (access and refresh)
    /// tokens. Persist this across restarts — when it is left unset the PDS generates a new
    /// random key on every process start, which invalidates every issued token and silently
    /// logs all clients out on restart or redeploy.
    /// </summary>
    /// <remarks>
    /// Should decode to at least 32 bytes. Generate one with
    /// <see cref="PdsSessionService.GenerateSigningKey"/> and store it as a secret
    /// (environment variable, secret store, …), for example:
    /// <c>options.SessionSigningKey = builder.Configuration["Pds:SessionSigningKey"];</c>
    /// </remarks>
    public string? SessionSigningKey { get; set; }

    /// <summary>
    /// Gets the resolved public URL. Falls back to https://{Hostname}.
    /// </summary>
    internal string ResolvedPublicUrl => (PublicUrl ?? $"https://{Hostname}").TrimEnd('/');
}
