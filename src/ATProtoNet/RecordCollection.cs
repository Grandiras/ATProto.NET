using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Models;
using ATProtoNet.Serialization;

namespace ATProtoNet;

/// <summary>
/// Base class for custom AT Protocol record types.
/// Extend this to define your own Lexicon record schemas.
/// </summary>
/// <remarks>
/// Fields a record carries but your class does not declare, such as those added by a newer
/// revision of the Lexicon or by another app, are kept in <see cref="LexObject.ExtensionData"/>
/// and written back by <see cref="RecordCollection{T}.PutAsync"/>, so a read-modify-write does
/// not drop them.
/// </remarks>
/// <example>
/// <code>
/// public class TodoItem : AtProtoRecord
/// {
///     [JsonPropertyName("$type")]
///     public override string Type => "com.example.todo.item";
///
///     [JsonPropertyName("title")]
///     public string Title { get; set; } = "";
///
///     [JsonPropertyName("completed")]
///     public bool Completed { get; set; }
///
///     [JsonPropertyName("dueDate")]
///     public AtDatetime? DueDate { get; set; }
/// }
/// </code>
/// </example>
public abstract class AtProtoRecord : LexObject
{
    /// <summary>
    /// The Lexicon type identifier (NSID#name) for this record.
    /// This corresponds to the <c>$type</c> field in AT Protocol.
    /// </summary>
    [JsonPropertyName("$type")]
    public abstract string Type { get; }

    /// <summary>
    /// Timestamp when the record was created. Set to the current time when the record object is
    /// created; a record read from the wire keeps the text it was written with.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; set; } = AtDatetime.Now();
}

/// <summary>
/// A strongly-typed collection interface for working with custom Lexicon records.
/// Provides a simple CRUD API for any record type stored in a PDS repository.
/// </summary>
/// <typeparam name="T">The record type, typically extending <see cref="AtProtoRecord"/>.</typeparam>
/// <remarks>
/// <para>This is the primary API for building custom AT Protocol applications.
/// Each <see cref="RecordCollection{T}"/> maps to a single Lexicon collection (NSID)
/// in the authenticated user's repository.</para>
/// <para>Your PDS stores data for any app that speaks AT Protocol — one account,
/// many apps, each with its own Lexicon namespace and collections.</para>
/// </remarks>
/// <example>
/// <code>
/// // Define your record type
/// public class TodoItem : AtProtoRecord
/// {
///     [JsonPropertyName("$type")]
///     public override string Type => "com.example.todo.item";
///
///     [JsonPropertyName("title")]
///     public string Title { get; set; } = "";
///
///     [JsonPropertyName("completed")]
///     public bool Completed { get; set; }
/// }
///
/// // Get a typed collection
/// var todos = client.GetCollection&lt;TodoItem&gt;(Nsid.Parse("com.example.todo.item"));
///
/// // CRUD operations
/// var created = await todos.CreateAsync(new TodoItem { Title = "Buy milk" });
/// var item = await todos.GetAsync(created.RecordKey);
/// await todos.PutAsync(created.RecordKey, item.Value with { Completed = true });
/// await todos.DeleteAsync(created.RecordKey);
///
/// // List / enumerate all records
/// var page = await todos.ListAsync(limit: 25);
/// await foreach (var record in todos.EnumerateAsync())
///     Console.WriteLine(record.Value.Title);
/// </code>
/// </example>
public sealed class RecordCollection<T> where T : class
{
    private readonly AtProtoClient _client;

    internal RecordCollection(AtProtoClient client, Nsid collection)
    {
        _client = client;
        Collection = collection;
    }

    /// <summary>
    /// The NSID of the collection (e.g., "com.example.todo.item").
    /// </summary>
    public Nsid Collection { get; }

    /// <summary>
    /// Create a new record in this collection.
    /// </summary>
    /// <param name="record">The record data to store.</param>
    /// <param name="rkey">Optional record key. If not specified, the server generates a TID.</param>
    /// <param name="validate">Whether to validate against the Lexicon schema on the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference containing the AT URI, CID, and parsed record key.</returns>
    public async Task<RecordRef> CreateAsync(
        T record,
        RecordKey? rkey = null,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        var response = await _client.Repo.CreateRecordAsync(
            _client.Did!, Collection, record, rkey, validate,
            cancellationToken: cancellationToken);

        return RecordRef.From("com.atproto.repo.createRecord", response.Uri, response.Cid);
    }

    /// <summary>
    /// Get a record by its record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized record with metadata.</returns>
    public Task<RecordView<T>> GetAsync(
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();
        return GetFromAsync(_client.Did!, rkey, cid, cancellationToken);
    }

    /// <summary>
    /// Get a record from any user's repository by DID and record key.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RecordView<T>> GetFromAsync(
        AtIdentifier repo,
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Repo.GetRecordAsync(
            repo, Collection, rkey, cid, cancellationToken);

        return ToView("com.atproto.repo.getRecord", response.Uri, response.Cid, response.Value);
    }

    /// <summary>
    /// Create or update a record at a specific record key (upsert).
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="record">The record data.</param>
    /// <param name="validate">Whether to validate against the Lexicon schema.</param>
    /// <param name="swapRecord">Optional CAS: the CID of the existing record to swap.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RecordRef> PutAsync(
        RecordKey rkey,
        T record,
        bool? validate = null,
        Cid? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        var response = await _client.Repo.PutRecordAsync(
            _client.Did!, Collection, rkey, record, validate, swapRecord,
            cancellationToken: cancellationToken);

        return RecordRef.From("com.atproto.repo.putRecord", response.Uri, response.Cid);
    }

    /// <summary>
    /// Delete a record by its record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="swapRecord">Optional CAS: the CID of the record version to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteAsync(
        RecordKey rkey,
        Cid? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        await _client.Repo.DeleteRecordAsync(
            _client.Did!, Collection, rkey, swapRecord,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List one page of records in this collection.
    /// </summary>
    /// <param name="reverse">Whether to reverse the sort order.</param>
    /// <param name="limit">Maximum number of records per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<RecordPage<T>> ListAsync(
        bool? reverse = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();
        return ListFromAsync(_client.Did!, reverse, limit, cursor, cancellationToken);
    }

    /// <summary>
    /// List one page of records from any user's repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="reverse">Whether to reverse the sort order.</param>
    /// <param name="limit">Maximum number of records per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RecordPage<T>> ListFromAsync(
        AtIdentifier repo,
        bool? reverse = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Repo.ListRecordsAsync(
            repo, Collection, reverse, limit, cursor, cancellationToken);

        return new RecordPage<T>
        {
            Records = [.. response.Records.Select(e => ToView("com.atproto.repo.listRecords", e.Uri, e.Cid, e.Value))],
            Cursor = response.Cursor,
        };
    }

    /// <summary>
    /// Enumerate every record in this collection, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Records per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RecordView<T>> EnumerateAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();
        return EnumerateFromAsync(_client.Did!, pageSize, cancellationToken);
    }

    /// <summary>
    /// Enumerate every record in this collection of any user's repository, fetching pages as
    /// needed.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="pageSize">Records per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RecordView<T>> EnumerateFromAsync(
        AtIdentifier repo,
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        Pagination.EnumerateAsync<RecordPage<T>, RecordView<T>>(
            (cursor, ct) => ListFromAsync(repo, limit: pageSize, cursor: cursor, cancellationToken: ct),
            cancellationToken);

    /// <summary>
    /// Check if a record exists at the given record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> ExistsAsync(
        RecordKey rkey, CancellationToken cancellationToken = default)
    {
        try
        {
            await GetAsync(rkey, cancellationToken: cancellationToken);
            return true;
        }
        catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
        {
            // Only the named error means absence. A malformed key or a missing repo is an
            // InvalidRequest too, and answering "false" for those would hide the real problem.
            return false;
        }
    }

    private static RecordView<T> ToView(string nsid, AtUri uri, Cid? cid, JsonElement value) => new()
    {
        Uri = uri,
        Cid = cid,
        Value = Deserialize(nsid, uri, value),
        RecordKey = uri.RecordKey
            ?? throw new XrpcResponseFormatException(nsid, $"Record URI {uri} has no record key."),
    };

    private static T Deserialize(string nsid, AtUri uri, JsonElement value)
    {
        try
        {
            return value.Deserialize<T>(AtProtoJsonDefaults.Options)
                ?? throw new XrpcResponseFormatException(nsid, $"Record {uri} is null.");
        }
        catch (JsonException ex)
        {
            throw new XrpcResponseFormatException(
                nsid, $"Record {uri} is not a valid {typeof(T).Name}: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// A reference to a created/updated record.
/// </summary>
public sealed class RecordRef
{
    /// <summary>The AT URI of the record.</summary>
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content hash) of the record.</summary>
    public required Cid Cid { get; init; }

    /// <summary>The record key portion of the URI.</summary>
    public required RecordKey RecordKey { get; init; }

    internal static RecordRef From(string nsid, AtUri uri, Cid cid) => new()
    {
        Uri = uri,
        Cid = cid,
        RecordKey = uri.RecordKey
            ?? throw new XrpcResponseFormatException(nsid, $"Record URI {uri} has no record key."),
    };
}

/// <summary>
/// A record fetched from the repository, with metadata.
/// </summary>
/// <typeparam name="T">The deserialized record type.</typeparam>
public sealed class RecordView<T>
{
    /// <summary>The AT URI of the record.</summary>
    public required AtUri Uri { get; init; }

    /// <summary>The CID (content hash) of the record.</summary>
    public Cid? Cid { get; init; }

    /// <summary>The deserialized record value.</summary>
    public required T Value { get; init; }

    /// <summary>The record key portion of the URI.</summary>
    public required RecordKey RecordKey { get; init; }
}

/// <summary>
/// A paginated page of records.
/// </summary>
/// <typeparam name="T">The deserialized record type.</typeparam>
public sealed class RecordPage<T> : ICursorPage<RecordView<T>>
{
    /// <summary>The records in this page.</summary>
    public required IReadOnlyList<RecordView<T>> Records { get; init; }

    /// <summary>Cursor for the next page. Null when no more results.</summary>
    public string? Cursor { get; init; }

    /// <summary>Whether there are more pages available.</summary>
    public bool HasMore => Cursor is not null;

    IReadOnlyList<RecordView<T>> ICursorPage<RecordView<T>>.Items => Records;
}
