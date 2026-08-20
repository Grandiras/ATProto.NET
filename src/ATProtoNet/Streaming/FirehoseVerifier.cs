using System.Formats.Cbor;
using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Sync;
using ATProtoNet.Repo;

namespace ATProtoNet.Streaming;

/// <summary>
/// Verifies the authenticity of firehose commit events against the AT Protocol specification.
/// Performs CID verification, commit signature verification, and optionally MST proof verification.
/// </summary>
public sealed class FirehoseVerifier : IDisposable
{
    private readonly DidResolver _didResolver;
    private readonly bool _ownsResolver;

    /// <summary>
    /// Creates a new verifier with a default DID resolver.
    /// </summary>
    public FirehoseVerifier()
    {
        _didResolver = new DidResolver();
        _ownsResolver = true;
    }

    /// <summary>
    /// Creates a new verifier with an existing DID resolver.
    /// </summary>
    /// <param name="didResolver">The DID resolver for fetching signing keys.</param>
    public FirehoseVerifier(DidResolver didResolver)
    {
        _didResolver = didResolver ?? throw new ArgumentNullException(nameof(didResolver));
        _ownsResolver = false;
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

        if (commit.Blocks is null || commit.Blocks.Length == 0)
            return VerificationResult.Failure("Commit has no blocks");

        try
        {
            var car = CarReader.FromBytes(commit.Blocks);
            return VerifyCarBlockCids(car);
        }
        catch (Exception ex)
        {
            return VerificationResult.Failure($"CID verification error: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies a commit event's signature against the account's signing key from the DID document.
    /// Performs: serialize unsigned commit as DAG-CBOR → SHA-256 → verify ECDSA signature.
    /// </summary>
    /// <param name="commit">The commit event with blocks.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A verification result.</returns>
    public async Task<VerificationResult> VerifySignatureAsync(
        CommitEvent commit, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commit);

        if (commit.Blocks is null || commit.Blocks.Length == 0)
            return VerificationResult.Failure("Commit has no blocks");

        try
        {
            // Parse the CAR file and find the commit block (first root). Verify
            // every block's CID against its bytes — a signed commit only binds the
            // commit block itself; subtree blocks must be checked separately or a
            // peer can swap record bytes while leaving the commit signature valid.
            var car = CarReader.FromBytes(commit.Blocks, verifyBlockCids: true);
            var rootBlock = car.GetRootBlock();
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

            // Resolve the DID document to get the signing key
            var didDoc = await _didResolver.ResolveDidAsync(commit.Repo, cancellationToken);
            var signingKey = didDoc.GetSigningKey();
            if (signingKey is null)
                return VerificationResult.Failure($"No atproto signing key found for {commit.Repo}");

            // Verify the ECDSA signature. AtProtoCrypto.VerifySignature hashes the
            // message internally (via ECDsa.VerifyData), so pass the RAW unsigned
            // commit bytes here, not a pre-computed digest — otherwise we'd verify
            // SHA256(SHA256(bytes)) against a signature over SHA256(bytes).
            var isValid = AtProtoCrypto.VerifySignature(signingKey, unsignedCborBytes, sigBytes);
            return isValid
                ? VerificationResult.Success()
                : VerificationResult.Failure("Signature verification failed");
        }
        catch (Exception ex)
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

        if (syncEvent.Blocks is null || syncEvent.Blocks.Length == 0)
            return VerificationResult.Failure("Sync event has no blocks");

        try
        {
            var car = CarReader.FromBytes(syncEvent.Blocks);
            return VerifyCarBlockCids(car);
        }
        catch (Exception ex)
        {
            return VerificationResult.Failure($"CID verification error: {ex.Message}");
        }
    }

    private static VerificationResult VerifyCarBlockCids(CarReader car)
    {
        foreach (var block in car.Blocks)
        {
            // Fail closed on UnknownCodec, matching CarReader.VerifyAllBlockCids.
            // AT Protocol only uses dag-cbor (0x71) and raw (0x55); anything else
            // from an untrusted source could smuggle blocks past CID verification
            // because the verifier can't recompute the digest under a codec it
            // doesn't recognize. Diverging policy here from the CarReader path
            // would let a hostile relay's commit pass the cheap pre-check while
            // failing the full signature verification — opposite verdicts on the
            // same input.
            switch (CarReader.VerifyBlockCid(block))
            {
                case BlockCidVerification.Mismatch:
                    return VerificationResult.Failure($"CID mismatch for block {block.CidHex}");
                case BlockCidVerification.UnknownCodec:
                    return VerificationResult.Failure(
                        $"Block {block.CidHex} uses an unsupported CID codec; " +
                        "AT Protocol only permits dag-cbor (0x71) and raw (0x55).");
            }
        }

        return VerificationResult.Success();
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
        if (_ownsResolver)
            _didResolver.Dispose();
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
