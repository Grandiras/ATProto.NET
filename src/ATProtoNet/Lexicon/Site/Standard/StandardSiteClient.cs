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
        string repo,
        PublicationRecord record,
        string? rkey = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.CreateRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.publication"), record, rkey is null ? null : RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a publication record.
    /// </summary>
    public Task<GetRecordResponse<PublicationRecord>> GetPublicationAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.GetRecordAsync<PublicationRecord>(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.publication"), RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update a publication record (put/upsert).
    /// </summary>
    public Task<PutRecordResponse> PutPublicationAsync(
        string repo,
        string rkey,
        PublicationRecord record,
        string? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.PutRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.publication"), RecordKey.Parse(rkey), record,
            swapRecord: swapRecord is null ? null : Cid.Parse(swapRecord), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a publication record.
    /// </summary>
    public Task<DeleteRecordResponse> DeletePublicationAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.publication"), RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List publication records in a repository.
    /// </summary>
    public Task<ListRecordsResponse> ListPublicationsAsync(
        string repo,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.ListRecordsAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.publication"), limit: limit, cursor: cursor,
            cancellationToken: cancellationToken);
    }

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
        string repo,
        DocumentRecord record,
        string? rkey = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.CreateRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.document"), record, rkey is null ? null : RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a document record.
    /// </summary>
    public Task<GetRecordResponse<DocumentRecord>> GetDocumentAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.GetRecordAsync<DocumentRecord>(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.document"), RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Update a document record (put/upsert).
    /// </summary>
    public Task<PutRecordResponse> PutDocumentAsync(
        string repo,
        string rkey,
        DocumentRecord record,
        string? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.PutRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.document"), RecordKey.Parse(rkey), record,
            swapRecord: swapRecord is null ? null : Cid.Parse(swapRecord), cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a document record.
    /// </summary>
    public Task<DeleteRecordResponse> DeleteDocumentAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.document"), RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List document records in a repository.
    /// </summary>
    public Task<ListRecordsResponse> ListDocumentsAsync(
        string repo,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.ListRecordsAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.document"), limit: limit, cursor: cursor,
            cancellationToken: cancellationToken);
    }

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
        string repo,
        SubscriptionRecord record,
        string? rkey = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.CreateRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.graph.subscription"), record, rkey is null ? null : RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a subscription record.
    /// </summary>
    public Task<GetRecordResponse<SubscriptionRecord>> GetSubscriptionAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.GetRecordAsync<SubscriptionRecord>(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.graph.subscription"), RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Unsubscribe from a publication (delete the subscription record).
    /// </summary>
    public Task<DeleteRecordResponse> DeleteSubscriptionAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.graph.subscription"), RecordKey.Parse(rkey),
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List subscription records in a repository.
    /// </summary>
    public Task<ListRecordsResponse> ListSubscriptionsAsync(
        string repo,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        return _repo.ListRecordsAsync(AtIdentifier.Parse(repo), Nsid.Parse("site.standard.graph.subscription"), limit: limit, cursor: cursor,
            cancellationToken: cancellationToken);
    }
}
