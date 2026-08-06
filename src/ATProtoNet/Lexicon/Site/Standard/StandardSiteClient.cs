using ATProtoNet.Http;
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
    private readonly XrpcClient _xrpc;
    private readonly RepoClient _repo;

    internal StandardSiteClient(XrpcClient xrpc, RepoClient repo)
    {
        _xrpc = xrpc;
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
        return _repo.CreateRecordAsync(repo, "site.standard.publication", record, rkey,
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
        return _repo.GetRecordAsync<PublicationRecord>(repo, "site.standard.publication", rkey,
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
        return _repo.PutRecordAsync(repo, "site.standard.publication", rkey, record,
            swapRecord: swapRecord, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a publication record.
    /// </summary>
    public Task<DeleteRecordResponse> DeletePublicationAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(repo, "site.standard.publication", rkey,
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
        return _repo.ListRecordsAsync(repo, "site.standard.publication", limit, cursor,
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
        return _repo.CreateRecordAsync(repo, "site.standard.document", record, rkey,
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
        return _repo.GetRecordAsync<DocumentRecord>(repo, "site.standard.document", rkey,
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
        return _repo.PutRecordAsync(repo, "site.standard.document", rkey, record,
            swapRecord: swapRecord, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Delete a document record.
    /// </summary>
    public Task<DeleteRecordResponse> DeleteDocumentAsync(
        string repo,
        string rkey,
        CancellationToken cancellationToken = default)
    {
        return _repo.DeleteRecordAsync(repo, "site.standard.document", rkey,
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
        return _repo.ListRecordsAsync(repo, "site.standard.document", limit, cursor,
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
        return _repo.CreateRecordAsync(repo, "site.standard.graph.subscription", record, rkey,
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
        return _repo.GetRecordAsync<SubscriptionRecord>(repo, "site.standard.graph.subscription", rkey,
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
        return _repo.DeleteRecordAsync(repo, "site.standard.graph.subscription", rkey,
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
        return _repo.ListRecordsAsync(repo, "site.standard.graph.subscription", limit, cursor,
            cancellationToken: cancellationToken);
    }
}
