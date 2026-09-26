using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Actor;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.Feed;
using ATProtoNet.Lexicon.App.Bsky.Graph;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Models;
using Microsoft.Extensions.Logging;

namespace ATProtoNet;

// The Bluesky record helpers: writes to the signed-in account's repository, kept apart from the
// sub-client registrations in AtProtoClient.cs.
public sealed partial class BlueskyClients
{
    private const int MaxProfileUpdateAttempts = 3;

    private AtProtoClient _client = null!;

    /// <summary>Connects the helpers to the client whose account they write to.</summary>
    internal void Bind(AtProtoClient client) => _client = client;

    /// <summary>
    /// Create a post (<c>app.bsky.feed.post</c>) in the signed-in account's repository.
    /// </summary>
    /// <param name="text">
    /// The text, with its facets. A <see cref="string"/> converts to text without facets; build
    /// mentions, links and hashtags with <see cref="RichTextBuilder"/>.
    /// </param>
    /// <param name="options">The embed, reply, languages, labels and tags, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the post.</returns>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    /// <example>
    /// <code>
    /// await client.Bsky.PostAsync("Hello, world!");
    ///
    /// var text = new RichTextBuilder().Text("Hi ").Mention(handle, did).Build();
    /// var post = await client.Bsky.PostAsync(text, new PostOptions { Langs = ["en"] });
    /// </code>
    /// </example>
    public Task<RecordRef> PostAsync(
        RichText text,
        PostOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        var did = _client.RequireDid();

        var post = new PostRecord
        {
            Text = text.Text,
            Facets = text.Facets.Count > 0 ? text.Facets : null,
            Embed = options?.Embed,
            Reply = options?.Reply,
            Langs = options?.Langs,
            Labels = options?.Labels,
            Tags = options?.Tags,
            CreatedAt = options?.CreatedAt ?? AtDatetime.Now(),
        };

        return _client.Repo.CreateRecordAsync(did, PostRecord.Collection, post, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Like a record (<c>app.bsky.feed.like</c>), typically a post.
    /// </summary>
    /// <param name="subject">The version of the record to like: its URI and CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the like; pass its <see cref="RecordRef.Uri"/> to <see cref="DeleteRecordAsync"/> to undo it.</returns>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    public Task<RecordRef> LikeAsync(StrongRef subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var did = _client.RequireDid();

        var like = new LikeRecord { Subject = subject, CreatedAt = AtDatetime.Now() };
        return _client.Repo.CreateRecordAsync(did, LikeRecord.Collection, like, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Repost a post (<c>app.bsky.feed.repost</c>).
    /// </summary>
    /// <param name="subject">The version of the post to repost: its URI and CID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the repost; pass its <see cref="RecordRef.Uri"/> to <see cref="DeleteRecordAsync"/> to undo it.</returns>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    public Task<RecordRef> RepostAsync(StrongRef subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var did = _client.RequireDid();

        var repost = new RepostRecord { Subject = subject, CreatedAt = AtDatetime.Now() };
        return _client.Repo.CreateRecordAsync(did, RepostRecord.Collection, repost, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Follow an account (<c>app.bsky.graph.follow</c>).
    /// </summary>
    /// <param name="subject">The DID of the account to follow.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the follow; pass its <see cref="RecordRef.Uri"/> to <see cref="DeleteRecordAsync"/> to unfollow.</returns>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    public Task<RecordRef> FollowAsync(Did subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var did = _client.RequireDid();

        var follow = new FollowRecord { Subject = subject, CreatedAt = AtDatetime.Now() };
        return _client.Repo.CreateRecordAsync(did, FollowRecord.Collection, follow, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a record: a post, or the like, repost or follow record that undoes a like, a repost
    /// or a follow.
    /// </summary>
    /// <param name="uri">
    /// The record's AT URI, such as <see cref="RecordRef.Uri"/>, <see cref="PostViewerState.Like"/>
    /// or <see cref="ViewerState.Following"/>. It must name a collection and a record key.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a record.</exception>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    public Task DeleteRecordAsync(AtUri uri, CancellationToken cancellationToken = default)
    {
        _ = RecordPaths.PathOf(uri);
        _client.RequireSession();
        return _client.Repo.DeleteRecordAsync(uri, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update the signed-in account's profile: read the current record, let
    /// <paramref name="update"/> edit it, and write it back.
    /// </summary>
    /// <param name="update">
    /// Edits the current profile in place, for example <c>p =&gt; p.DisplayName = "Alice"</c>.
    /// Setting a property to <see langword="null"/> removes the field. It may run more than once
    /// (see remarks), each time on a freshly read record.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference to the written profile record.</returns>
    /// <remarks>
    /// <para>Every field <paramref name="update"/> leaves alone is written back unchanged,
    /// including fields this SDK version does not model (kept in
    /// <see cref="LexObject.ExtensionData"/>).</para>
    /// <para>The write is guarded by <c>swapRecord</c>, so a concurrent edit from another client is
    /// never silently overwritten. If one lands between the read and the write, the whole
    /// read-edit-write is retried, up to three attempts in total, after which the
    /// <see cref="XrpcErrors.InvalidSwap"/> <see cref="XrpcException"/> is rethrown. When the account has no
    /// profile yet, <paramref name="update"/> receives an empty record with <c>createdAt</c> set,
    /// and the write carries no swap guard.</para>
    /// </remarks>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    /// <exception cref="XrpcResponseFormatException">The stored profile is not a valid profile record.</exception>
    public async Task<RecordRef> UpdateProfileAsync(
        Action<ProfileRecord> update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        var did = _client.RequireDid();

        for (var attempt = 1; ; attempt++)
        {
            var (profile, cid) = await GetProfileRecordAsync(did, cancellationToken);
            update(profile);

            try
            {
                return await _client.Repo.PutRecordAsync(
                    did, ProfileRecord.Collection, RecordKey.Self, profile,
                    swapRecord: cid,
                    cancellationToken: cancellationToken);
            }
            catch (XrpcException ex) when (ex.Is(XrpcErrors.InvalidSwap)
                                           && attempt < MaxProfileUpdateAttempts)
            {
                _client.Logger.LogDebug(
                    "Profile changed concurrently (attempt {Attempt} of {Max}); re-reading",
                    attempt, MaxProfileUpdateAttempts);
            }
        }
    }

    /// <summary>
    /// Reads the account's profile record and the CID to swap against, or a fresh record and
    /// <see langword="null"/> when there is none.
    /// </summary>
    private async Task<(ProfileRecord Profile, Cid? Cid)> GetProfileRecordAsync(
        Did did, CancellationToken cancellationToken)
    {
        try
        {
            var existing = await _client.Repo.GetRecordAsync<ProfileRecord>(
                did, ProfileRecord.Collection, RecordKey.Self, cancellationToken: cancellationToken);

            return (existing.Value, existing.Cid);
        }
        catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
        {
            return (new ProfileRecord { CreatedAt = AtDatetime.Now() }, null);
        }
    }
}

/// <summary>
/// The optional parts of a post created with <see cref="BlueskyClients.PostAsync"/>.
/// </summary>
public sealed record PostOptions
{
    /// <summary>Embedded content: images, a link card, a quoted record, a video.</summary>
    public EmbedBase? Embed { get; init; }

    /// <summary>The post this one replies to, and the root of its thread.</summary>
    public ReplyRef? Reply { get; init; }

    /// <summary>The languages the post is written in (BCP-47 tags).</summary>
    public IReadOnlyList<string>? Langs { get; init; }

    /// <summary>Self-applied labels, such as content warnings.</summary>
    public SelfLabels? Labels { get; init; }

    /// <summary>Additional hashtags not in the text (up to 8).</summary>
    public IReadOnlyList<string>? Tags { get; init; }

    /// <summary>The creation time to record; <see langword="null"/> for now.</summary>
    public AtDatetime? CreatedAt { get; init; }
}
