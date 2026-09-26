using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Labeling;
using ATProtoNet.Lexicon.Com.AtProto.Label;
using ATProtoNet.Models;
using ATProtoNet.Server.Xrpc;
using Microsoft.AspNetCore.Http;

namespace ATProtoNet.Server.Labeling;

/// <summary>Query parameters for <c>com.atproto.label.queryLabels</c>.</summary>
public sealed class QueryLabelsParameters
{
    /// <summary>
    /// The subjects to match: full URIs, prefixes ending in <c>*</c>, or <c>*</c> for all.
    /// </summary>
    [JsonPropertyName("uriPatterns")]
    public required IReadOnlyList<string> UriPatterns { get; init; }

    /// <summary>The labelers whose labels to return; all when omitted.</summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<Did>? Sources { get; init; }

    /// <summary>The page size (1 to 250, default 50).</summary>
    [JsonPropertyName("limit")]
    public int? Limit { get; init; }

    /// <summary>The cursor of the previous page.</summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }
}

/// <summary>
/// Serves <c>com.atproto.label.queryLabels</c> for a labeler, from the registered
/// <see cref="ILabelSource"/>.
/// </summary>
/// <remarks>
/// <para>Validates the request as the reference labeler does — a pattern may hold <c>*</c> only
/// at its end, and <c>limit</c> ranges from 1 to 250 (larger values are served as 250) — then
/// asks the source for a page.</para>
/// <para>When a <see cref="LabelSigner"/> is registered, the labeler's labels the source returns
/// without a signature are signed on the way out, so every label this labeler serves carries the
/// <c>ver</c> and <c>sig</c> the spec requires between services. Labels from other sources are
/// returned as they are.</para>
/// <para>The Lexicon describes the endpoint as public; require authorization on it, or on the
/// <c>/xrpc</c> group, only for a labeler that is not.</para>
/// </remarks>
/// <example>
/// <code>
/// builder.Services.AddSingleton&lt;ILabelSource, MyLabelSource&gt;();
/// builder.Services.AddSingleton(new LabelSigner(labelerDid, labelKey));
/// builder.Services.AddXrpcEndpoint&lt;QueryLabelsEndpoint&gt;();
/// // …
/// app.MapXrpcEndpoints();
/// </code>
/// </example>
public sealed class QueryLabelsEndpoint : IXrpcQuery<QueryLabelsParameters, QueryLabelsResponse>
{
    /// <summary>The page size when a request names none.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The largest page served.</summary>
    public const int MaxLimit = 250;

    private readonly ILabelSource _source;
    private readonly LabelSigner? _signer;

    /// <summary>
    /// Creates the endpoint.
    /// </summary>
    /// <param name="source">The labeler's labels.</param>
    /// <param name="signer">
    /// Signs the labeler's unsigned labels on the way out, or <see langword="null"/> to serve
    /// labels as the source returns them.
    /// </param>
    public QueryLabelsEndpoint(ILabelSource source, LabelSigner? signer = null)
    {
        ArgumentNullException.ThrowIfNull(source);

        _source = source;
        _signer = signer;
    }

    /// <inheritdoc/>
    public static Nsid Nsid { get; } = Nsid.Parse("com.atproto.label.queryLabels");

    /// <inheritdoc/>
    public async Task<QueryLabelsResponse> HandleAsync(
        QueryLabelsParameters parameters, HttpContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var query = new LabelQuery
        {
            UriPatterns = ValidatePatterns(parameters.UriPatterns),
            Sources = parameters.Sources ?? [],
            Limit = ValidateLimit(parameters.Limit),
            Cursor = parameters.Cursor,
        };

        var page = await _source.QueryLabelsAsync(query, cancellationToken);
        if (_signer is not { } signer || !page.Labels.Any(NeedsSignature))
            return page;

        return new QueryLabelsResponse
        {
            Cursor = page.Cursor,
            Labels = [.. page.Labels.Select(label => NeedsSignature(label) ? signer.Sign(label) : label)],
        };
    }

    private bool NeedsSignature(Label label) => label.Sig is not { Length: > 0 } && label.Src == _signer!.Labeler;

    private static IReadOnlyList<string> ValidatePatterns(IReadOnlyList<string>? patterns)
    {
        if (patterns is null || patterns.Count == 0)
            throw new XrpcException(XrpcErrors.InvalidRequest, "The \"uriPatterns\" parameter is required.");

        foreach (var pattern in patterns)
        {
            if (string.IsNullOrEmpty(pattern))
                throw new XrpcException(XrpcErrors.InvalidRequest, "A URI pattern is empty.");

            var wildcard = pattern.IndexOf('*', StringComparison.Ordinal);
            if (wildcard >= 0 && wildcard != pattern.Length - 1)
            {
                throw new XrpcException(
                    XrpcErrors.InvalidRequest, $"Invalid URI pattern '{pattern}': '*' may only end a pattern.");
            }
        }

        return patterns;
    }

    private static int ValidateLimit(int? limit) => limit switch
    {
        null => DefaultLimit,
        < 1 => throw new XrpcException(XrpcErrors.InvalidRequest, "The \"limit\" parameter must be at least 1."),
        _ => Math.Min(limit.Value, MaxLimit),
    };
}
