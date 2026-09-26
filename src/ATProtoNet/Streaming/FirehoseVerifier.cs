using System.Formats.Cbor;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;

namespace ATProtoNet.Streaming;

/// <summary>
/// Verifies firehose events: that every block of a commit's CAR matches its CID, and that the
/// commit is signed by the account's current signing key.
/// </summary>
/// <remarks>
/// <para>It does not invert the commit's operations against the previous MST root
/// (<c>prevData</c>), so it does not prove that the operations listed are the ones the signed
/// commit made, nor that no commit was missed. <see cref="RepoSyncVerifier"/> does both: the
/// Sync 1.1 checks.</para>
/// <para>Signature verification reads each account's signing key from its DID document, so the
/// resolver must cache: a firehose carries thousands of commits a second from far fewer
/// accounts. The default one does, and a cached key costs no network at all. Keep the cache
/// current by passing each <c>#identity</c> event to <see cref="InvalidateIdentityAsync"/>
/// (<see cref="TypedFirehoseConsumer"/> does); a signature that fails against a cached key is
/// retried once against a refreshed document, as the sync spec requires.</para>
/// </remarks>
public sealed class FirehoseVerifier : IDisposable
{
    private readonly IDidResolver _didResolver;
    private readonly bool _ownsResolver;

    /// <summary>
    /// Creates a verifier over its own <see cref="CachingDidResolver"/>.
    /// </summary>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    public FirehoseVerifier(IdentityResolverOptions? options = null)
    {
        _didResolver = new CachingDidResolver(options);
        _ownsResolver = true;
    }

    /// <summary>
    /// Creates a verifier over an existing DID resolver, which the caller owns.
    /// </summary>
    /// <param name="didResolver">
    /// Resolves signing keys. Pass a <see cref="CachingDidResolver"/>: an uncached resolver makes a
    /// directory request for every commit.
    /// </param>
    public FirehoseVerifier(IDidResolver didResolver)
    {
        _didResolver = didResolver ?? throw new ArgumentNullException(nameof(didResolver));
        _ownsResolver = false;
    }

    /// <summary>
    /// Drops any cached DID document for an account, so its next commit is verified against a
    /// freshly resolved key. Call it for every <c>#identity</c> event.
    /// </summary>
    /// <param name="did">The account whose identity changed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task InvalidateIdentityAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);
        return _didResolver.InvalidateAsync(did, cancellationToken);
    }

    /// <summary>
    /// Verifies a commit event's CID integrity — that block CIDs match their content.
    /// This is a local-only check that does not require network access.
    /// </summary>
    /// <param name="commit">The commit event with blocks.</param>
    /// <returns>A verification result.</returns>
    public static VerificationResult VerifyCid(CommitEvent commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        return ReadVerifiedCar(commit.Blocks, "Commit", out _);
    }

    /// <summary>
    /// Verifies a commit event's signature against the account's signing key from the DID
    /// document, and every block of its CAR against its CID; a passing result makes a separate
    /// <see cref="VerifyCid(CommitEvent)"/> unnecessary.
    /// Performs: serialize unsigned commit as DAG-CBOR → SHA-256 → verify ECDSA signature.
    /// </summary>
    /// <param name="commit">The commit event with blocks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A verification result.</returns>
    public async Task<VerificationResult> VerifySignatureAsync(
        CommitEvent commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        // Every block's CID is checked against its bytes: a signed commit only binds the commit
        // block itself, so otherwise a peer could swap record bytes and leave the signature valid.
        var cids = ReadVerifiedCar(commit.Blocks, "Commit", out var car);
        if (!cids.IsValid)
            return cids;

        try
        {
            // The commit block is the CAR's first root.
            var rootBlock = car!.GetRootBlock();
            if (rootBlock is null)
                return VerificationResult.Failure("No root block in CAR");

            // Build the unsigned commit by splicing the `sig` key/value out of the
            // ORIGINAL CBOR bytes, not by round-tripping through JSON. Re-encoding via
            // JsonObject is lossy: CBOR integer widths, byte-string vs `$bytes` shape,
            // CID tag-42 vs `$link` shape, and length-first-lex key ordering may not
            // be preserved, producing a different hash than the signer used.
            var splice = ExtractSignedView(rootBlock.Data);
            if (splice is null)
                return VerificationResult.Failure("Could not decode commit block");

            var (unsignedCborBytes, sigBytes) = splice.Value;
            if (sigBytes is null || sigBytes.Length == 0)
                return VerificationResult.Failure("Commit has empty signature");

            var didDoc = await _didResolver.ResolveAsync(commit.Repo, cancellationToken);
            var signingKey = didDoc.GetSigningKey();
            if (signingKey is null)
                return VerificationResult.Failure($"No atproto signing key found for {commit.Repo}");

            // AtProtoCrypto.VerifySignature hashes the message itself (via ECDsa.VerifyData), so
            // it gets the RAW unsigned commit bytes, not a digest — otherwise it would verify
            // SHA256(SHA256(bytes)) against a signature over SHA256(bytes). The parsed key is
            // cached by did:key, so a cached document means no key parsing either.
            if (AtProtoCrypto.VerifySignature(signingKey, unsignedCborBytes, sigBytes))
                return VerificationResult.Success();

            // The cached document may predate a key rotation: refetch once before declaring the
            // signature bad. The resolver rate-limits refreshes, so forged commits cannot turn
            // into a directory request each.
            var refreshedKey = (await _didResolver.RefreshAsync(commit.Repo, cancellationToken)).GetSigningKey();
            if (refreshedKey is not null &&
                !string.Equals(refreshedKey, signingKey, StringComparison.Ordinal) &&
                AtProtoCrypto.VerifySignature(refreshedKey, unsignedCborBytes, sigBytes))
            {
                return VerificationResult.Success();
            }

            return VerificationResult.Failure("Signature verification failed");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return VerificationResult.Failure($"Signature verification error: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies a sync event's CID integrity.
    /// </summary>
    /// <param name="syncEvent">The sync event with blocks.</param>
    /// <returns>A verification result.</returns>
    public static VerificationResult VerifyCid(SyncEvent syncEvent)
    {
        ArgumentNullException.ThrowIfNull(syncEvent);
        return ReadVerifiedCar(syncEvent.Blocks, "Sync event", out _);
    }

    /// <summary>
    /// Parses an event's CAR and checks every block against its CID, failing closed on a codec
    /// other than dag-cbor or raw, which could otherwise carry blocks past the check.
    /// </summary>
    private static VerificationResult ReadVerifiedCar(byte[]? blocks, string what, out CarReader? car)
    {
        car = null;
        if (blocks is null || blocks.Length == 0)
            return VerificationResult.Failure($"{what} has no blocks");

        try
        {
            car = CarReader.FromBytes(blocks, verifyBlockCids: true);
            return VerificationResult.Success();
        }
        catch (FormatException ex)
        {
            return VerificationResult.Failure($"CID verification error: {ex.Message}");
        }
    }

    /// <summary>
    /// Walks a commit-block's DAG-CBOR bytes, captures the value at the <c>sig</c>
    /// key, and returns a new CBOR byte sequence representing the same map with
    /// the <c>sig</c> key/value pair removed (map header entry count decremented).
    /// </summary>
    /// <remarks>
    /// This preserves the original byte-for-byte encoding of every other field, so
    /// the SHA-256 of the result matches what the signer hashed.
    /// </remarks>
    /// <returns>
    /// A tuple of <c>(unsignedCborBytes, sigBytes)</c>, or <c>null</c> if the input
    /// is not a CBOR map or has no <c>sig</c> field.
    /// </returns>
    internal static (byte[] UnsignedBytes, byte[]? SigBytes)? ExtractSignedView(byte[] commitCbor)
    {
        if (commitCbor.Length == 0)
            return null;

        // Walk the map with the framework reader to find each key/value pair's byte
        // range. We can't mutate CBOR with CborReader, but we can ask it to skip
        // values to learn how many bytes each pair consumed and then slice the
        // original buffer byte-for-byte.
        //
        // Use Strict (not Ctap2Canonical): DAG-CBOR REQUIRES CBOR tag 42 to encode
        // CIDs, and atproto commits always carry `data` (and often `prev`) as tag-42
        // values. Ctap2Canonical forbids all tags and would throw on every real
        // commit. Canonical-form integrity is preserved here by the byte-for-byte
        // splice of the original buffer; the reader is only used to discover field
        // boundaries.
        var reader = new CborReader(commitCbor, CborConformanceMode.Strict);
        int entryCount;
        var pairs = new List<(int Start, int Length, string Key)>();
        int sigPairIndex = -1;
        byte[]? sigBytes = null;

        // Wrap the entire walk in try/catch — a hostile commit can encode keys
        // or values in ways that throw mid-loop (non-text key, sig encoded as a
        // non-byte-string, truncated buffer). Returning null here surfaces as
        // "Could not decode commit block" rather than the framework's CBOR
        // exception text, so consumers can distinguish malformed commits from
        // transient errors without parsing exception strings.
        try
        {
            var entryCountNullable = reader.ReadStartMap();
            if (entryCountNullable is not { } parsed)
                return null; // Indefinite-length maps are forbidden by DAG-CBOR.
            entryCount = parsed;

            for (var i = 0; i < entryCount; i++)
            {
                var pairStart = commitCbor.Length - reader.BytesRemaining;
                var key = reader.ReadTextString();

                if (key == "sig")
                {
                    // The sig MUST be a CBOR byte string (DAG-CBOR major type 2).
                    // Anything else is a malformed commit; fail closed.
                    if (reader.PeekState() != CborReaderState.ByteString)
                        return null;
                    sigPairIndex = i;
                    sigBytes = reader.ReadByteString();
                }
                else
                {
                    reader.SkipValue();
                }

                int pairEnd = commitCbor.Length - reader.BytesRemaining;
                pairs.Add((pairStart, pairEnd - pairStart, key));
            }
        }
        catch (CborContentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            // CborReader throws InvalidOperationException for state-machine
            // misuse (e.g. ReadTextString when the next item isn't a text
            // string). Treat as malformed input.
            return null;
        }

        if (sigPairIndex < 0)
            return null;

        // Compose the unsigned bytes: new map header (entryCount - 1) + every pair
        // except the sig pair, preserved byte-for-byte from the original. The exact size is
        // known up front, so write it straight into one array rather than growing a
        // MemoryStream and copying the result back out of it.
        var newCount = entryCount - 1;
        var bodyLength = 0;
        for (var i = 0; i < pairs.Count; i++)
        {
            if (i != sigPairIndex) bodyLength += pairs[i].Length;
        }

        var unsigned = new byte[MapHeaderLength(newCount) + bodyLength];
        var at = WriteMapHeader(unsigned, newCount);
        for (var i = 0; i < pairs.Count; i++)
        {
            if (i == sigPairIndex) continue;
            commitCbor.AsSpan(pairs[i].Start, pairs[i].Length).CopyTo(unsigned.AsSpan(at));
            at += pairs[i].Length;
        }

        return (unsigned, sigBytes);
    }

    /// <summary>The number of bytes <see cref="WriteMapHeader"/> emits for a given entry count.</summary>
    internal static int MapHeaderLength(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return count switch
        {
            < 24 => 1,
            < 256 => 2,
            < 65536 => 3,
            _ => 5,
        };
    }

    /// <summary>
    /// Writes a CBOR map header for <paramref name="count"/> entries into
    /// <paramref name="destination"/>, and returns the number of bytes written.
    /// </summary>
    internal static int WriteMapHeader(Span<byte> destination, int count)
    {
        const int majorType5 = 5 << 5;
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Map entry count cannot be negative.");

        if (count < 24)
        {
            destination[0] = (byte)(majorType5 | count);
            return 1;
        }

        if (count < 256)
        {
            destination[0] = (byte)(majorType5 | 24);
            destination[1] = (byte)count;
            return 2;
        }

        if (count < 65536)
        {
            destination[0] = (byte)(majorType5 | 25);
            destination[1] = (byte)((count >> 8) & 0xFF);
            destination[2] = (byte)(count & 0xFF);
            return 3;
        }

        // 4-byte length (CBOR 0x1a). Fail closed at int max — DAG-CBOR maps
        // larger than this would also push the splice well past anything a
        // real atproto commit could hold, and silently truncating to 16 bits
        // (the prior behavior) would produce a malformed header whose count
        // doesn't match the bytes that follow, hashing to garbage.
        destination[0] = (byte)(majorType5 | 26);
        destination[1] = (byte)((count >> 24) & 0xFF);
        destination[2] = (byte)((count >> 16) & 0xFF);
        destination[3] = (byte)((count >> 8) & 0xFF);
        destination[4] = (byte)(count & 0xFF);
        return 5;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_ownsResolver && _didResolver is IDisposable disposable)
            disposable.Dispose();
    }
}

/// <summary>
/// Represents the result of a firehose verification operation.
/// </summary>
public sealed class VerificationResult
{
    /// <summary>Whether the verification passed.</summary>
    public bool IsValid { get; }

    /// <summary>Error message if verification failed.</summary>
    public string? Error { get; }

    private VerificationResult(bool isValid, string? error)
    {
        IsValid = isValid;
        Error = error;
    }

    internal static VerificationResult Success() => new(true, null);
    internal static VerificationResult Failure(string error) => new(false, error);

    /// <inheritdoc/>
    public override string ToString() => IsValid ? "Valid" : $"Invalid: {Error}";
}
