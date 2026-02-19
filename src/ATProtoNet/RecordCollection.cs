using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Serialization;

namespace ATProtoNet;

/// <summary>
/// Base class for custom AT Protocol record types.
/// Extend this to define your own Lexicon record schemas.
/// </summary>
/// <example>
/// <code>
/// public class TodoItem : AtProtoRecord
/// {
///     public override string Type => "com.example.todo.item";
///     
///     [JsonPropertyName("title")]
///     public string Title { get; set; } = "";
///     
///     [JsonPropertyName("completed")]
///     public bool Completed { get; set; }
///     
///     [JsonPropertyName("dueDate")]
///     public string? DueDate { get; set; }
/// }
/// </code>
/// </example>
public abstract class AtProtoRecord
{
    /// <summary>
    /// The Lexicon type identifier (NSID#name) for this record.
    /// This corresponds to the <c>$type</c> field in AT Protocol.
    /// </summary>
    [JsonPropertyName("$type")]
    public abstract string Type { get; }

    /// <summary>
    /// Timestamp when the record was created (ISO 8601).
    /// Automatically set to UTC now when not provided.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("o");
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
/// var todos = client.GetCollection&lt;TodoItem&gt;("com.example.todo.item");
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
    private readonly string _collection;

    internal RecordCollection(AtProtoClient client, string collection)
    {
        _client = client;
        _collection = collection;
    }

    /// <summary>
    /// The NSID of the collection (e.g., "com.example.todo.item").
    /// </summary>
    public string Collection => _collection;

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
        string? rkey = null,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        var response = await _client.Repo.CreateRecordAsync(
            _client.Did!, _collection, record, rkey, validate,
            cancellationToken: cancellationToken);

        return RecordRef.FromCreateResponse(response);
    }

    /// <summary>
    /// Get a record by its record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized record with metadata.</returns>
    public async Task<RecordView<T>> GetAsync(
        string rkey,
        string? cid = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        var response = await _client.Repo.GetRecordAsync(
            _client.Did!, _collection, rkey, cid, cancellationToken);

        var value = response.Value.Deserialize<T>(AtProtoJsonDefaults.Options)
            ?? throw new InvalidOperationException($"Failed to deserialize record to {typeof(T).Name}");

        return new RecordView<T>
        {
            Uri = response.Uri,
            Cid = response.Cid,
            Value = value,
            RecordKey = AtUri.Parse(response.Uri).RecordKey!,
        };
    }

    /// <summary>
    /// Get a record from any user's repository by DID and record key.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RecordView<T>> GetFromAsync(
        string repo,
        string rkey,
        string? cid = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Repo.GetRecordAsync(
            repo, _collection, rkey, cid, cancellationToken);

        var value = response.Value.Deserialize<T>(AtProtoJsonDefaults.Options)
            ?? throw new InvalidOperationException($"Failed to deserialize record to {typeof(T).Name}");

        return new RecordView<T>
        {
            Uri = response.Uri,
            Cid = response.Cid,
            Value = value,
            RecordKey = AtUri.Parse(response.Uri).RecordKey!,
        };
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
        string rkey,
        T record,
        bool? validate = null,
        string? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        var response = await _client.Repo.PutRecordAsync(
            _client.Did!, _collection, rkey, record, validate, swapRecord,
            cancellationToken: cancellationToken);

        return new RecordRef
        {
            Uri = response.Uri,
            Cid = response.Cid,
            RecordKey = AtUri.Parse(response.Uri).RecordKey!,
        };
    }

    /// <summary>
    /// Delete a record by its record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="swapRecord">Optional CAS: the CID of the record version to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task DeleteAsync(
        string rkey,
        string? swapRecord = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        await _client.Repo.DeleteRecordAsync(
            _client.Did!, _collection, rkey, swapRecord,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List records in this collection, with pagination.
    /// </summary>
    /// <param name="limit">Maximum number of records per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="reverse">Whether to reverse the sort order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RecordPage<T>> ListAsync(
        int? limit = null,
        string? cursor = null,
        bool? reverse = null,
        CancellationToken cancellationToken = default)
    {
        _client.EnsureAuthenticated();

        var response = await _client.Repo.ListRecordsAsync(
            _client.Did!, _collection, limit, cursor, reverse, cancellationToken);

        var records = response.Records
            .Select(entry =>
            {
                var value = entry.Value.Deserialize<T>(AtProtoJsonDefaults.Options);
                return new RecordView<T>
                {
                    Uri = entry.Uri,
                    Cid = entry.Cid,
                    Value = value!,
                    RecordKey = AtUri.Parse(entry.Uri).RecordKey!,
                };
            })
            .ToList();

        return new RecordPage<T>
        {
            Records = records,
            Cursor = response.Cursor,
        };
    }

    /// <summary>
    /// List records from any user's repository.
    /// </summary>
    public async Task<RecordPage<T>> ListFromAsync(
        string repo,
        int? limit = null,
        string? cursor = null,
        bool? reverse = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _client.Repo.ListRecordsAsync(
            repo, _collection, limit, cursor, reverse, cancellationToken);

        var records = response.Records
            .Select(entry =>
            {
                var value = entry.Value.Deserialize<T>(AtProtoJsonDefaults.Options);
                return new RecordView<T>
                {
                    Uri = entry.Uri,
                    Cid = entry.Cid,
                    Value = value!,
                    RecordKey = AtUri.Parse(entry.Uri).RecordKey!,
                };
            })
            .ToList();

        return new RecordPage<T>
        {
            Records = records,
            Cursor = response.Cursor,
        };
    }

    /// <summary>
    /// Enumerate all records in this collection using automatic pagination.
    /// </summary>
    /// <param name="pageSize">Number of records to fetch per page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async IAsyncEnumerable<RecordView<T>> EnumerateAsync(
        int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await ListAsync(pageSize, cursor, cancellationToken: cancellationToken);
            foreach (var record in page.Records)
                yield return record;

            cursor = page.Cursor;
        } while (cursor is not null);
    }

    /// <summary>
    /// Enumerate all records in any user's repository collection.
    /// </summary>
    public async IAsyncEnumerable<RecordView<T>> EnumerateFromAsync(
        string repo,
        int pageSize = 100,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        string? cursor = null;
        do
        {
            var page = await ListFromAsync(repo, pageSize, cursor, cancellationToken: cancellationToken);
            foreach (var record in page.Records)
                yield return record;

            cursor = page.Cursor;
        } while (cursor is not null);
    }

    /// <summary>
    /// Check if a record exists at the given record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<bool> ExistsAsync(
        string rkey, CancellationToken cancellationToken = default)
    {
        try
        {
            await GetAsync(rkey, cancellationToken: cancellationToken);
            return true;
        }
        catch (AtProtoHttpException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.BadRequest
            && ex.ErrorType is "RecordNotFound" or "InvalidRequest")
        {
            return false;
        }
    }
}

/// <summary>
/// A reference to a created/updated record.
/// </summary>
public sealed class RecordRef
{
    /// <summary>The AT URI of the record.</summary>
    public required string Uri { get; init; }

    /// <summary>The CID (content hash) of the record.</summary>
    public required string Cid { get; init; }

    /// <summary>The record key portion of the URI.</summary>
    public required string RecordKey { get; init; }

    internal static RecordRef FromCreateResponse(CreateRecordResponse response) => new()
    {
        Uri = response.Uri,
        Cid = response.Cid,
        RecordKey = AtUri.Parse(response.Uri).RecordKey!,
    };
}

/// <summary>
/// A record fetched from the repository, with metadata.
/// </summary>
/// <typeparam name="T">The deserialized record type.</typeparam>
public sealed class RecordView<T>
{
    /// <summary>The AT URI of the record.</summary>
    public required string Uri { get; init; }

    /// <summary>The CID (content hash) of the record.</summary>
    public string? Cid { get; init; }

    /// <summary>The deserialized record value.</summary>
    public required T Value { get; init; }

    /// <summary>The record key portion of the URI.</summary>
    public required string RecordKey { get; init; }
}

/// <summary>
/// A paginated page of records.
/// </summary>
/// <typeparam name="T">The deserialized record type.</typeparam>
public sealed class RecordPage<T>
{
    /// <summary>The records in this page.</summary>
    public required List<RecordView<T>> Records { get; init; }

    /// <summary>Cursor for the next page. Null when no more results.</summary>
    public string? Cursor { get; init; }

    /// <summary>Whether there are more pages available.</summary>
    public bool HasMore => Cursor is not null;
}
