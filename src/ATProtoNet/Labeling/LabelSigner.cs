using ATProtoNet.Crypto;
using ATProtoNet.Identity;
using ATProtoNet.Models;

namespace ATProtoNet.Labeling;

/// <summary>
/// Signs a labeler's labels with its <c>#atproto_label</c> key, as the
/// <see href="https://atproto.com/specs/label">label spec</see> requires of every label a service
/// hands to another.
/// </summary>
/// <remarks>
/// <para>A signed label carries <c>ver</c> 1 and a low-S <c>sig</c> over the rest of its fields
/// (<see cref="LabelSigning.GetSigningBytes"/>). As the reference labeler (<c>@atproto/ozone</c>)
/// does, a <c>neg</c> of <see langword="false"/> is dropped before signing, so the label reads
/// the same whether or not the field was set.</para>
/// <para>Safe for concurrent use, so one instance can serve a whole labeler. The key stays the
/// caller's to dispose.</para>
/// <para>The spec asks a labeler to record which key signed each label, so labels signed before
/// a key rotation can be told apart; <see cref="SigningKey"/> is that key.</para>
/// </remarks>
/// <example>
/// <code>
/// using var key = AtProtoCrypto.ImportPrivateKey(pkcs8, KeyCurve.K256);
/// var signer = new LabelSigner(Did.Parse("did:plc:mylabeler"), key);
///
/// Label label = signer.Sign("at://did:plc:alice/app.bsky.feed.post/3l6oveex3ii2l", "spam");
/// </code>
/// </example>
public sealed class LabelSigner
{
    private readonly AtProtoKey _key;
    private readonly TimeProvider _timeProvider;
    private readonly Lock _lock = new();

    /// <summary>
    /// Creates a signer.
    /// </summary>
    /// <param name="labeler">The labeler's DID: the <c>src</c> of every label this signs.</param>
    /// <param name="key">
    /// The private key the labeler's DID document publishes as <c>#atproto_label</c>.
    /// </param>
    /// <param name="timeProvider">The clock that stamps <c>cts</c>. Defaults to the system clock.</param>
    public LabelSigner(Did labeler, AtProtoKey key, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(labeler);
        ArgumentNullException.ThrowIfNull(key);

        Labeler = labeler;
        _key = key;
        _timeProvider = timeProvider ?? TimeProvider.System;
        SigningKey = key.ToDidKey();
    }

    /// <summary>The labeler's DID: the <c>src</c> of every label this signs.</summary>
    public Did Labeler { get; }

    /// <summary>
    /// The public half of the signing key as a <c>did:key</c>: what the labeler's DID document
    /// must publish as <c>#atproto_label</c> for its labels to verify.
    /// </summary>
    public string SigningKey { get; }

    /// <summary>
    /// Creates and signs a label, stamped with the current time.
    /// </summary>
    /// <param name="subject">
    /// What is labelled: an <c>at://</c> URI for a record, or a DID for an account.
    /// </param>
    /// <param name="value">The label value, such as <c>spam</c> or <c>!hide</c>. At most 128 UTF-8 bytes.</param>
    /// <param name="cid">The version of the record the label applies to, or <see langword="null"/> for every version.</param>
    /// <param name="negate">Whether this label retracts an earlier one with the same subject and value.</param>
    /// <param name="expiresAt">When the label stops applying, or <see langword="null"/> for never.</param>
    /// <returns>The signed label.</returns>
    /// <exception cref="ArgumentException">A field is empty, or the value too long.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key has no private half.</exception>
    public Label Sign(string subject, string value, Cid? cid = null, bool negate = false, AtDatetime? expiresAt = null) =>
        Sign(new Label
        {
            Src = Labeler,
            Uri = subject,
            Cid = cid,
            Val = value,
            Neg = negate,
            Cts = AtDatetime.FromDateTimeOffset(_timeProvider.GetUtcNow()),
            Exp = expiresAt,
        });

    /// <summary>
    /// Signs a label.
    /// </summary>
    /// <param name="label">
    /// The label, whose <c>src</c> must be <see cref="Labeler"/>. Any <c>sig</c> it carries is
    /// replaced.
    /// </param>
    /// <returns>
    /// A new label with <c>ver</c> 1 and the signature. Fields the Lexicon does not declare are
    /// not carried over, since the signature would not cover them.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// The label is another labeler's, carries a <c>ver</c> other than 1, has an empty subject or
    /// value, a value longer than 128 UTF-8 bytes, or a <c>cts</c> or <c>exp</c> that is not a
    /// valid datetime.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The key has no private half.</exception>
    public Label Sign(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);
        Validate(label);

        var unsigned = new Label
        {
            Version = LabelSigning.Version,
            Src = label.Src,
            Uri = label.Uri,
            Cid = label.Cid,
            Val = label.Val,
            Neg = label.Neg == true ? true : null,
            Cts = label.Cts,
            Exp = label.Exp,
        };

        var bytes = LabelSigning.GetSigningBytes(unsigned);

        // .NET does not document ECDsa as safe for concurrent use, and one signer serves a
        // whole labeler.
        byte[] signature;
        lock (_lock)
            signature = _key.Sign(bytes);

        return new Label
        {
            Version = unsigned.Version,
            Src = unsigned.Src,
            Uri = unsigned.Uri,
            Cid = unsigned.Cid,
            Val = unsigned.Val,
            Neg = unsigned.Neg,
            Cts = unsigned.Cts,
            Exp = unsigned.Exp,
            Sig = signature,
        };
    }

    private void Validate(Label label)
    {
        if (label.Src != Labeler)
        {
            throw new ArgumentException(
                $"The label's src is '{label.Src}'; this signer signs only {Labeler}'s labels.", nameof(label));
        }

        if (label.Version is { } version && version != LabelSigning.Version)
        {
            throw new ArgumentException(
                $"The label's ver is {version}; only version {LabelSigning.Version} is defined.", nameof(label));
        }

        if (string.IsNullOrEmpty(label.Uri))
            throw new ArgumentException("The label has no subject (uri).", nameof(label));

        if (string.IsNullOrEmpty(label.Val))
            throw new ArgumentException("The label has no value (val).", nameof(label));

        if (LabelSigning.ValueLength(label.Val) > LabelSigning.MaxValueLength)
        {
            throw new ArgumentException(
                $"The label's value is longer than {LabelSigning.MaxValueLength} UTF-8 bytes.", nameof(label));
        }

        if (!label.Cts.IsValid)
            throw new ArgumentException($"The label's cts '{label.Cts}' is not a valid datetime.", nameof(label));

        if (label.Exp is { IsValid: false } exp)
            throw new ArgumentException($"The label's exp '{exp}' is not a valid datetime.", nameof(label));
    }
}
