using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Labeling;

/// <summary>The outcome of verifying a label's signature.</summary>
public enum LabelVerificationStatus
{
    /// <summary>The signature verifies against the issuer's <c>#atproto_label</c> key.</summary>
    Valid,

    /// <summary>The label carries no signature.</summary>
    Unsigned,

    /// <summary>
    /// The label's <c>ver</c> is missing or not 1, the only version defined. A signed label must
    /// carry it.
    /// </summary>
    UnsupportedVersion,

    /// <summary>
    /// The label lacks a field every label carries, or holds text that cannot be encoded, so no
    /// signature can cover it.
    /// </summary>
    Malformed,

    /// <summary>
    /// The issuer's DID document publishes no <c>#atproto_label</c> key, or one this SDK cannot
    /// read, even after a refetch.
    /// </summary>
    NoLabelKey,

    /// <summary>
    /// The issuer's DID (the label's <c>src</c>) could not be resolved. See
    /// <see cref="LabelVerificationResult.Error"/>.
    /// </summary>
    IssuerUnresolved,

    /// <summary>
    /// The signature does not verify against the issuer's current key, even after a refetch.
    /// </summary>
    /// <remarks>
    /// A labeler that rotated its key may not have re-signed labels issued under the old one, and
    /// the spec accepts that; such a label reads as invalid until it is.
    /// </remarks>
    InvalidSignature,
}

/// <summary>
/// A label and the outcome of verifying its signature.
/// </summary>
public sealed class LabelVerificationResult
{
    /// <summary>The label that was verified.</summary>
    public required Label Label { get; init; }

    /// <summary>The outcome.</summary>
    public required LabelVerificationStatus Status { get; init; }

    /// <summary>Whether the signature verified.</summary>
    public bool IsValid => Status == LabelVerificationStatus.Valid;

    /// <summary>
    /// The issuer's <c>#atproto_label</c> key the signature was checked against, as a
    /// <c>did:key</c>, or <see langword="null"/> when none was reached.
    /// </summary>
    public string? SigningKey { get; init; }

    /// <summary>
    /// Why the issuer could not be resolved, when <see cref="Status"/> is
    /// <see cref="LabelVerificationStatus.IssuerUnresolved"/>.
    /// </summary>
    public DidResolutionException? Error { get; init; }
}

/// <summary>
/// Verifies labels against the <c>#atproto_label</c> key their issuer's DID document publishes,
/// as the <see href="https://atproto.com/specs/label">label spec</see> asks of a service that
/// receives labels from another.
/// </summary>
/// <remarks>
/// <para>The issuer is the label's <c>src</c>, resolved through the <see cref="IDidResolver"/>.
/// A signature that fails against the cached document's key is retried once against a refetched
/// document, since the labeler may have rotated its key: the re-resolution the spec asks for. A
/// caching resolver rate-limits refetches per DID, so a stream of forged labels cannot turn into
/// a directory request each; use one (<see cref="CachingDidResolver"/>, or the resolver
/// <c>AddAtProtoIdentity()</c> registers).</para>
/// <para>Verification reports rather than throws: every outcome is a
/// <see cref="LabelVerificationStatus"/>, and only cancellation escapes. What to do with a label
/// that did not verify is the caller's choice.</para>
/// <para>A verified label is authentic, not necessarily current: check <see cref="Label.Exp"/>,
/// and whether a later negation retracted it.</para>
/// </remarks>
/// <example>
/// <code>
/// var verifier = new LabelVerifier(resolver);
/// var page = await client.Label.QueryLabelsAsync(["at://did:plc:alice/*"]);
/// foreach (var result in await verifier.VerifyAllAsync(page.Labels))
/// {
///     if (result.IsValid)
///         Apply(result.Label);
/// }
/// </code>
/// </example>
public sealed class LabelVerifier
{
    private readonly IDidResolver _resolver;

    /// <summary>
    /// Creates a verifier.
    /// </summary>
    /// <param name="resolver">
    /// Resolves each label's issuer. Use a caching one: every label resolves a document.
    /// </param>
    public LabelVerifier(IDidResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        _resolver = resolver;
    }

    /// <summary>
    /// Verifies a label's signature against its issuer's <c>#atproto_label</c> key.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The outcome.</returns>
    public async Task<LabelVerificationResult> VerifyAsync(Label label, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (LabelSigning.Prepare(label, out var bytes) is { } refused)
            return Result(label, refused);

        var signature = label.Sig!;
        string? key;
        try
        {
            key = await ResolveKeyAsync(label.Src, refresh: false, cancellationToken).ConfigureAwait(false);
            if (key is not null && LabelSigning.VerifyWith(key, bytes, signature))
                return Result(label, LabelVerificationStatus.Valid, key);

            // The cached document may predate a key rotation, or the key's publication: refetch
            // once before refusing.
            var refreshed = await ResolveKeyAsync(label.Src, refresh: true, cancellationToken).ConfigureAwait(false);
            if (refreshed is null)
                return Result(label, LabelVerificationStatus.NoLabelKey);

            if (!string.Equals(refreshed, key, StringComparison.Ordinal) &&
                LabelSigning.VerifyWith(refreshed, bytes, signature))
            {
                return Result(label, LabelVerificationStatus.Valid, refreshed);
            }

            return Result(label, LabelVerificationStatus.InvalidSignature, refreshed);
        }
        catch (DidResolutionException ex)
        {
            return new LabelVerificationResult
            {
                Label = label,
                Status = LabelVerificationStatus.IssuerUnresolved,
                Error = ex,
            };
        }
    }

    /// <summary>
    /// Verifies each of a set of labels, such as a <c>queryLabels</c> page.
    /// </summary>
    /// <param name="labels">The labels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One result per label, in the same order.</returns>
    public async Task<IReadOnlyList<LabelVerificationResult>> VerifyAllAsync(
        IEnumerable<Label> labels, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(labels);

        var results = labels.TryGetNonEnumeratedCount(out var count)
            ? new List<LabelVerificationResult>(count)
            : [];

        // One at a time: the labels of a page or an event usually share an issuer, whose document
        // the first resolution caches for the rest.
        foreach (var label in labels)
            results.Add(await VerifyAsync(label, cancellationToken).ConfigureAwait(false));

        return results;
    }

    /// <summary>
    /// The issuer's label key, or <see langword="null"/> when its document publishes none, or one
    /// this SDK cannot read.
    /// </summary>
    private async Task<string?> ResolveKeyAsync(Did issuer, bool refresh, CancellationToken cancellationToken)
    {
        var document = refresh
            ? await _resolver.RefreshAsync(issuer, cancellationToken).ConfigureAwait(false)
            : await _resolver.ResolveAsync(issuer, cancellationToken).ConfigureAwait(false);

        return document.TryGetVerificationKey(DidDocument.LabelKeyId, out var key) == DidDocumentEntryStatus.Found
            ? key
            : null;
    }

    private static LabelVerificationResult Result(Label label, LabelVerificationStatus status, string? key = null) =>
        new() { Label = label, Status = status, SigningKey = key };
}
