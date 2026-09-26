using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Models;

namespace ATProtoNet.Server.Labeling;

/// <summary>
/// Where <see cref="QueryLabelsEndpoint"/> reads a labeler's labels from: the application's own
/// label storage.
/// </summary>
public interface ILabelSource
{
    /// <summary>
    /// Finds the labels a <c>com.atproto.label.queryLabels</c> request asks for.
    /// </summary>
    /// <param name="query">The validated request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Up to <see cref="LabelQuery.Limit"/> matching labels, oldest first, and a cursor that
    /// resumes after the last one, or <see langword="null"/> when there are no more.
    /// </returns>
    /// <remarks>
    /// <para>Return labels as stored, signed or not: the endpoint signs this labeler's unsigned
    /// ones on the way out when a <see cref="ATProtoNet.Labeling.LabelSigner"/> is registered.
    /// Storing the signature, and which key made it, saves signing on every query and is what the
    /// spec asks of a labeler that may rotate its key.</para>
    /// <para>The cursor is opaque to the endpoint. Throw an
    /// <see cref="ATProtoNet.Http.XrpcException"/> with <c>InvalidRequest</c> for one this
    /// source did not issue.</para>
    /// </remarks>
    Task<QueryLabelsResponse> QueryLabelsAsync(LabelQuery query, CancellationToken cancellationToken = default);
}

/// <summary>
/// A validated <c>com.atproto.label.queryLabels</c> request.
/// </summary>
public sealed class LabelQuery
{
    /// <summary>The pattern that matches every subject.</summary>
    public const string MatchAll = "*";

    /// <summary>
    /// The subjects to match, any of them: a full URI or DID, matched exactly; a prefix ending in
    /// <c>*</c>, matching every subject that starts with the text before it; or <c>*</c> alone,
    /// matching every subject. Validated: <c>*</c> appears only at the end.
    /// </summary>
    public required IReadOnlyList<string> UriPatterns { get; init; }

    /// <summary>The labelers whose labels to return, or empty for every labeler's.</summary>
    public IReadOnlyList<Did> Sources { get; init; } = [];

    /// <summary>The page size: 1 to 250, 50 unless the request named one.</summary>
    public required int Limit { get; init; }

    /// <summary>The cursor of the previous page, as the source issued it, or <see langword="null"/>.</summary>
    public string? Cursor { get; init; }

    /// <summary>Whether a pattern matches every subject, so the subject need not be filtered on.</summary>
    public bool MatchesAllSubjects => UriPatterns.Contains(MatchAll, StringComparer.Ordinal);

    /// <summary>
    /// Whether a label matches the patterns and sources, for a source that filters in memory.
    /// </summary>
    /// <param name="label">The label.</param>
    /// <returns>Whether the label belongs in the response.</returns>
    public bool Matches(Label label)
    {
        ArgumentNullException.ThrowIfNull(label);

        if (Sources.Count > 0 && !Sources.Contains(label.Src))
            return false;

        foreach (var pattern in UriPatterns)
        {
            var matches = pattern.EndsWith('*')
                ? label.Uri.AsSpan().StartsWith(pattern.AsSpan(0, pattern.Length - 1), StringComparison.Ordinal)
                : string.Equals(label.Uri, pattern, StringComparison.Ordinal);

            if (matches)
                return true;
        }

        return false;
    }
}
