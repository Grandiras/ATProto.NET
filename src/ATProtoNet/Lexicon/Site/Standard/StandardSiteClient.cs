using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Lexicon.Site.Standard.Document;
using ATProtoNet.Lexicon.Site.Standard.Graph;
using ATProtoNet.Lexicon.Site.Standard.Publication;

namespace ATProtoNet.Lexicon.Site.Standard;

/// <summary>
/// Client for Standard.site lexicons — long-form publishing on AT Protocol.
/// Provides convenience methods for managing publications, documents, and subscriptions
/// using the underlying repo record operations.
/// </summary>
public sealed class StandardSiteClient
{
    private static readonly Nsid PublicationCollection = Nsid.Parse("site.standard.publication");
    private static readonly Nsid DocumentCollection = Nsid.Parse("site.standard.document");
    private static readonly Nsid SubscriptionCollection = Nsid.Parse("site.standard.graph.subscription");

    private readonly RepoClient _repo;

    internal StandardSiteClient(RepoClient repo)
    {
        _repo = repo;
    }

    // ──────────────────────────────────────────────────────────
    //  Publications
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a publication record.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="record">The publication record to create.</param>
    /// <param name="rkey">Optional record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateRecordResponse> CreatePublicationAsync(
        AtIdentifier repo,
        PublicationRecord record,
        RecordKey? rkey = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.CreateRecordAsync(repo, PublicationCollection, record, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a publication record.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRecordResponse<PublicationRecord>> GetPublicationAsync(
        AtIdentifier repo,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.GetRecordAsync<PublicationRecord>(repo, PublicationCollection, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the publication record an AT URI names, such as a
    /// <see cref="SubscriptionRecord.Publication"/>.
    /// </summary>
    /// <param name="uri">The publication's AT URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a publication record.</exception>
    public Task<GetRecordResponse<PublicationRecord>> GetPublicationAsync(
        AtUri uri,
        CancellationToken cancellationToken = default)
    {
        var rkey = RecordKeyOf(uri, PublicationCollection);
        return GetPublicationAsync(uri.Repo, rkey, cancellationToken);
    }

    /// <summary>
    /// Update a publication record (put/upsert).
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="record">The publication record to write.</param>
    /// <param name="swapRecord">Optional compare-and-swap guard: the CID the record must be at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PutRecordResponse> PutPublicationAsync(
        AtIdentifier repo,
        RecordKey rkey,
        PublicationRecord record,
        Cid? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.PutRecordAsync(repo, PublicationCollection, rkey, record,
            swapRecord: swapRecord, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a publication record.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DeleteRecordResponse> DeletePublicationAsync(
        AtIdentifier repo,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(repo, PublicationCollection, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the publication records in a repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="limit">Maximum number of records (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<RecordPage<PublicationRecord>> ListPublicationsAsync(
        AtIdentifier repo,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return ListAsync<PublicationRecord>(repo, PublicationCollection, limit, cursor, cancellationToken);
    }

    /// <summary>
    /// Enumerate every publication record in a repository, fetching pages as needed.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="pageSize">Records per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RecordView<PublicationRecord>> EnumeratePublicationsAsync(
        AtIdentifier repo,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<RecordPage<PublicationRecord>, RecordView<PublicationRecord>>(
            (cursor, ct) => ListPublicationsAsync(repo, pageSize, cursor, ct),
            cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Documents
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create a document record.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="record">The document record to create.</param>
    /// <param name="rkey">Optional record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateRecordResponse> CreateDocumentAsync(
        AtIdentifier repo,
        DocumentRecord record,
        RecordKey? rkey = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.CreateRecordAsync(repo, DocumentCollection, record, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a document record.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRecordResponse<DocumentRecord>> GetDocumentAsync(
        AtIdentifier repo,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.GetRecordAsync<DocumentRecord>(repo, DocumentCollection, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the document record an AT URI names.
    /// </summary>
    /// <param name="uri">The document's AT URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a document record.</exception>
    public Task<GetRecordResponse<DocumentRecord>> GetDocumentAsync(
        AtUri uri,
        CancellationToken cancellationToken = default)
    {
        var rkey = RecordKeyOf(uri, DocumentCollection);
        return GetDocumentAsync(uri.Repo, rkey, cancellationToken);
    }

    /// <summary>
    /// Update a document record (put/upsert).
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="record">The document record to write.</param>
    /// <param name="swapRecord">Optional compare-and-swap guard: the CID the record must be at.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<PutRecordResponse> PutDocumentAsync(
        AtIdentifier repo,
        RecordKey rkey,
        DocumentRecord record,
        Cid? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.PutRecordAsync(repo, DocumentCollection, rkey, record,
            swapRecord: swapRecord, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a document record.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DeleteRecordResponse> DeleteDocumentAsync(
        AtIdentifier repo,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(repo, DocumentCollection, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the document records in a repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="limit">Maximum number of records (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<RecordPage<DocumentRecord>> ListDocumentsAsync(
        AtIdentifier repo,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return ListAsync<DocumentRecord>(repo, DocumentCollection, limit, cursor, cancellationToken);
    }

    /// <summary>
    /// Enumerate every document record in a repository, fetching pages as needed.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="pageSize">Records per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RecordView<DocumentRecord>> EnumerateDocumentsAsync(
        AtIdentifier repo,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<RecordPage<DocumentRecord>, RecordView<DocumentRecord>>(
            (cursor, ct) => ListDocumentsAsync(repo, pageSize, cursor, ct),
            cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Subscriptions
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Subscribe to a publication.
    /// </summary>
    /// <param name="repo">The DID or handle of the subscriber.</param>
    /// <param name="record">The subscription record.</param>
    /// <param name="rkey">Optional record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<CreateRecordResponse> CreateSubscriptionAsync(
        AtIdentifier repo,
        SubscriptionRecord record,
        RecordKey? rkey = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.CreateRecordAsync(repo, SubscriptionCollection, record, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a subscription record.
    /// </summary>
    /// <param name="repo">The DID or handle of the subscriber.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetRecordResponse<SubscriptionRecord>> GetSubscriptionAsync(
        AtIdentifier repo,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.GetRecordAsync<SubscriptionRecord>(repo, SubscriptionCollection, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the subscription record an AT URI names.
    /// </summary>
    /// <param name="uri">The subscription's AT URI.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a subscription record.</exception>
    public Task<GetRecordResponse<SubscriptionRecord>> GetSubscriptionAsync(
        AtUri uri,
        CancellationToken cancellationToken = default)
    {
        var rkey = RecordKeyOf(uri, SubscriptionCollection);
        return GetSubscriptionAsync(uri.Repo, rkey, cancellationToken);
    }

    /// <summary>
    /// Unsubscribe from a publication (delete the subscription record).
    /// </summary>
    /// <param name="repo">The DID or handle of the subscriber.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<DeleteRecordResponse> DeleteSubscriptionAsync(
        AtIdentifier repo,
        RecordKey rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(repo, SubscriptionCollection, rkey,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of the subscription records in a repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the subscriber.</param>
    /// <param name="limit">Maximum number of records (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<RecordPage<SubscriptionRecord>> ListSubscriptionsAsync(
        AtIdentifier repo,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return ListAsync<SubscriptionRecord>(repo, SubscriptionCollection, limit, cursor, cancellationToken);
    }

    /// <summary>
    /// Enumerate every subscription record in a repository, fetching pages as needed.
    /// </summary>
    /// <param name="repo">The DID or handle of the subscriber.</param>
    /// <param name="pageSize">Records per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RecordView<SubscriptionRecord>> EnumerateSubscriptionsAsync(
        AtIdentifier repo,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<RecordPage<SubscriptionRecord>, RecordView<SubscriptionRecord>>(
            (cursor, ct) => ListSubscriptionsAsync(repo, pageSize, cursor, ct),
            cancellationToken);

    private async Task<RecordPage<T>> ListAsync<T>(
        AtIdentifier repo, Nsid collection, int? limit, string? cursor, CancellationToken cancellationToken)
        where T : class
    {
        var response = await _repo.ListRecordsAsync(
            repo, collection, limit: limit, cursor: cursor, cancellationToken: cancellationToken).ConfigureAwait(false);
        return RecordCollection<T>.ToPage(response);
    }

    private static RecordKey RecordKeyOf(AtUri uri, Nsid collection)
    {
        ArgumentNullException.ThrowIfNull(uri);
        return uri.Collection == collection && uri.RecordKey is { } rkey
            ? rkey
            : throw new ArgumentException($"'{uri}' does not name a {collection} record.", nameof(uri));
    }
}
