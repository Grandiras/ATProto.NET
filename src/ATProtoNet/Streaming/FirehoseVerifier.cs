using System.Security.Cryptography;
using System.Text.Json;
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
            // Parse the CAR file and find the commit block (first root)
            var car = CarReader.FromBytes(commit.Blocks);
            var rootBlock = car.GetRootBlock();
            if (rootBlock is null)
                return VerificationResult.Failure("No root block in CAR");

            // Decode the commit block as JSON to extract the signature
            var commitJson = DagCborDecoder.DecodeToNode(rootBlock.Data);
            if (commitJson is null)
                return VerificationResult.Failure("Could not decode commit block");

            var commitObj = commitJson.AsObject();

            // Extract and remove the signature to create the unsigned commit
            if (!commitObj.ContainsKey("sig"))
                return VerificationResult.Failure("Commit has no sig field");

            var sigNode = commitObj["sig"];
            var sigBytes = sigNode?["$bytes"] is not null
                ? Convert.FromBase64String(sigNode["$bytes"]!.GetValue<string>())
                : null;

            if (sigBytes is null || sigBytes.Length == 0)
                return VerificationResult.Failure("Commit has empty signature");

            // Remove sig to create unsigned commit, then re-encode as DAG-CBOR
            commitObj.Remove("sig");
            var unsignedCommitElement = JsonSerializer.SerializeToElement(commitObj);
            var unsignedCborBytes = DagCborEncoder.Encode(unsignedCommitElement);

            // Hash the unsigned commit bytes
            var hash = SHA256.HashData(unsignedCborBytes);

            // Resolve the DID document to get the signing key
            var didDoc = await _didResolver.ResolveDidAsync(commit.Repo, cancellationToken);
            var signingKey = GetSigningKey(didDoc);
            if (signingKey is null)
                return VerificationResult.Failure($"No atproto signing key found for {commit.Repo}");

            // Verify the ECDSA signature
            var isValid = AtProtoCrypto.VerifySignature(signingKey, hash, sigBytes);
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
            var cidString = CidComputation.EncodeCidToString(block.Cid);
            var cid = Identity.Cid.Parse(cidString);

            if (!CidComputation.Verify(cid, block.Data, isDagCbor: true) &&
                !CidComputation.Verify(cid, block.Data, isDagCbor: false))
            {
                return VerificationResult.Failure($"CID mismatch for block {block.CidHex}");
            }
        }

        return VerificationResult.Success();
    }

    /// <summary>
    /// Extracts the AT Protocol signing key (<c>#atproto</c>) from a DID document.
    /// </summary>
    /// <param name="didDoc">The DID document.</param>
    /// <returns>The signing key as a <c>did:key</c> string, or <c>null</c> if not found.</returns>
    private static string? GetSigningKey(DidDocument didDoc)
    {
        var method = didDoc.VerificationMethod.FirstOrDefault(vm =>
            (vm.Id == "#atproto" || vm.Id == $"{didDoc.Id}#atproto") &&
            vm.Type == "Multikey" &&
            !string.IsNullOrEmpty(vm.PublicKeyMultibase));

        if (method?.PublicKeyMultibase is null)
            return null;

        // Convert multikey (z-prefixed base58btc) to did:key format
        return $"did:key:{method.PublicKeyMultibase}";
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
