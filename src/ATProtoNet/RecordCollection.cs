using System.Reflection;
using System.Text.Json.Serialization;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Repo;
using ATProtoNet.Models;

namespace ATProtoNet;

/// <summary>
/// A record type that names the collection it is stored in, so
/// <see cref="AtProtoClient.GetCollection{T}()"/> needs no NSID.
/// </summary>
/// <example>
/// <code>
/// public sealed class TodoItem : AtProtoRecord, IAtProtoRecord
/// {
///     public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.item");
///
///     public override string Type => Collection;
///
///     [JsonPropertyName("title")]
///     public string Title { get; set; } = "";
/// }
///
/// var todos = client.GetCollection&lt;TodoItem&gt;();
/// </code>
/// </example>
public interface IAtProtoRecord
{
    /// <summary>
    /// The NSID of the collection records of this type are stored in, which is also the
    /// <c>$type</c> they are written with.
    /// </summary>
    static abstract Nsid Collection { get; }
}

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
/// public class TodoItem : AtProtoRecord, IAtProtoRecord
/// {
///     public static Nsid Collection { get; } = Nsid.Parse("com.example.todo.item");
///
///     public override string Type => Collection;
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
    /// When the record was created. <see cref="RecordCollection{T}.CreateAsync"/> sets it to the
    /// current time when it is <see langword="null"/>; a record read from the wire keeps the text
    /// it was written with, and stays <see langword="null"/> when it was written without one.
    /// </summary>
    [JsonPropertyName("createdAt")]
    public AtDatetime? CreatedAt { get; set; }
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
/// // Get a typed collection for a record type that implements IAtProtoRecord
/// var todos = client.GetCollection&lt;TodoItem&gt;();
///
/// // CRUD operations
/// var created = await todos.CreateAsync(new TodoItem { Title = "Buy milk" });
/// var item = await todos.GetAsync(created.RecordKey);
/// item.Value.Completed = true;
/// await todos.PutAsync(created.RecordKey, item.Value, swapRecord: item.Cid);
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
    /// <param name="record">
    /// The record data to store. An <see cref="AtProtoRecord"/> whose
    /// <see cref="AtProtoRecord.CreatedAt"/> is <see langword="null"/> has it set to the current
    /// time before it is sent.
    /// </param>
    /// <param name="rkey">Optional record key. If not specified, the server generates a TID.</param>
    /// <param name="validate">Whether to validate against the Lexicon schema on the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A reference containing the AT URI, CID, and parsed record key.</returns>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    public Task<RecordRef> CreateAsync(
        T record,
        RecordKey? rkey = null,
        bool? validate = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var did = _client.RequireDid();

        if (record is AtProtoRecord { CreatedAt: null } stamped)
            stamped.CreatedAt = AtDatetime.Now();

        return _client.Repo.CreateRecordAsync(
            did, Collection, record, rkey, validate,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get a record by its record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The deserialized record with metadata.</returns>
    /// <exception cref="XrpcException">
    /// The service refused the call; <see cref="XrpcErrors.RecordNotFound"/> when there is no such
    /// record (see <see cref="FindAsync"/>).
    /// </exception>
    public Task<RecordView<T>> GetAsync(
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default) =>
        GetFromAsync(_client.RequireDid(), rkey, cid, cancellationToken);

    /// <summary>
    /// Get a record from any user's repository by DID and record key.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">
    /// The service refused the call; <see cref="XrpcErrors.RecordNotFound"/> when there is no such
    /// record (see <see cref="FindFromAsync"/>).
    /// </exception>
    public Task<RecordView<T>> GetFromAsync(
        AtIdentifier repo,
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default) =>
        _client.Repo.GetRecordAsync<T>(repo, Collection, rkey, cid, cancellationToken);

    /// <summary>
    /// Get a record by its record key, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> on <see cref="XrpcErrors.RecordNotFound"/>.</returns>
    /// <exception cref="XrpcException">
    /// Any other refusal, such as a malformed key or a missing repository: only the named error
    /// means the record is absent.
    /// </exception>
    public Task<RecordView<T>?> FindAsync(
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default) =>
        FindFromAsync(_client.RequireDid(), rkey, cid, cancellationToken);

    /// <summary>
    /// Get a record from any user's repository, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="rkey">The record key.</param>
    /// <param name="cid">Optional CID for a specific version.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The record, or <see langword="null"/> on <see cref="XrpcErrors.RecordNotFound"/>.</returns>
    /// <exception cref="XrpcException">
    /// Any other refusal, such as a malformed key or a missing repository: only the named error
    /// means the record is absent.
    /// </exception>
    public async Task<RecordView<T>?> FindFromAsync(
        AtIdentifier repo,
        RecordKey rkey,
        Cid? cid = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await GetFromAsync(repo, rkey, cid, cancellationToken);
        }
        catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
        {
            return null;
        }
    }

    /// <summary>
    /// Create or update a record at a specific record key (upsert).
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="record">The record data, written as it is.</param>
    /// <param name="validate">Whether to validate against the Lexicon schema.</param>
    /// <param name="swapRecord">Optional CAS: the CID of the existing record to swap.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    public Task<RecordRef> PutAsync(
        RecordKey rkey,
        T record,
        bool? validate = null,
        Cid? swapRecord = null,
        CancellationToken cancellationToken = default) =>
        _client.Repo.PutRecordAsync(
            _client.RequireDid(), Collection, rkey, record, validate, swapRecord,
            cancellationToken: cancellationToken);

    /// <summary>
    /// Delete a record by its record key.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="swapRecord">Optional CAS: the CID of the record version to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcAuthenticationException">No session is installed.</exception>
    public Task DeleteAsync(
        RecordKey rkey,
        Cid? swapRecord = null,
        CancellationToken cancellationToken = default) =>
        _client.Repo.DeleteRecordAsync(
            _client.RequireDid(), Collection, rkey, swapRecord,
            cancellationToken: cancellationToken);

    /// <summary>
    /// List one page of records in this collection.
    /// </summary>
    /// <param name="reverse">Whether to reverse the sort order.</param>
    /// <param name="limit">Maximum number of records per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcResponseFormatException">A record on the page is not a valid <typeparamref name="T"/>.</exception>
    public Task<RecordPage<T>> ListAsync(
        bool? reverse = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        ListFromAsync(_client.RequireDid(), reverse, limit, cursor, cancellationToken);

    /// <summary>
    /// List one page of records from any user's repository.
    /// </summary>
    /// <param name="repo">The DID or handle of the repo owner.</param>
    /// <param name="reverse">Whether to reverse the sort order.</param>
    /// <param name="limit">Maximum number of records per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor from a previous response.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcResponseFormatException">A record on the page is not a valid <typeparamref name="T"/>.</exception>
    public Task<RecordPage<T>> ListFromAsync(
        AtIdentifier repo,
        bool? reverse = null,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default) =>
        _client.Repo.ListRecordsAsync<T>(repo, Collection, reverse, limit, cursor, cancellationToken);

    /// <summary>
    /// Enumerate every record in this collection, fetching pages as needed.
    /// </summary>
    /// <param name="pageSize">Records per request (1-100); <see langword="null"/> for the server default.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public IAsyncEnumerable<RecordView<T>> EnumerateAsync(
        int? pageSize = null,
        CancellationToken cancellationToken = default) =>
        EnumerateFromAsync(_client.RequireDid(), pageSize, cancellationToken);

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
    /// Check if a record exists at the given record key: <see cref="FindAsync"/> is not
    /// <see langword="null"/>.
    /// </summary>
    /// <param name="rkey">The record key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="XrpcException">
    /// Any refusal other than <see cref="XrpcErrors.RecordNotFound"/>, as for <see cref="FindAsync"/>.
    /// </exception>
    public async Task<bool> ExistsAsync(RecordKey rkey, CancellationToken cancellationToken = default) =>
        await FindAsync(rkey, cancellationToken: cancellationToken) is not null;
}

/// <summary>
/// A reference to a record that was just written: its AT URI, the CID of the version written,
/// and the commit that wrote it.
/// </summary>
public sealed record RecordRef
{
    /// <summary>Creates a record reference.</summary>
    /// <param name="uri">The record's AT URI; it must name a collection and a record key.</param>
    /// <param name="cid">The CID of the record version.</param>
    /// <param name="commit">The commit the write was applied in, when the service reported it.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a record.</exception>
    public RecordRef(AtUri uri, Cid cid, CommitMeta? commit = null)
    {
        ArgumentNullException.ThrowIfNull(uri);
        ArgumentNullException.ThrowIfNull(cid);

        RecordKey = RecordPaths.RecordKeyOf(uri);
        Uri = uri;
        Cid = cid;
        Commit = commit;
    }

    /// <summary>The AT URI of the record.</summary>
    public AtUri Uri { get; }

    /// <summary>The CID (content hash) of the record version.</summary>
    public Cid Cid { get; }

    /// <summary>The commit the write was applied in, when the service reported it.</summary>
    public CommitMeta? Commit { get; }

    /// <summary>The record key portion of the URI.</summary>
    public RecordKey RecordKey { get; }

    /// <summary>
    /// Whether the service validated the record against its Lexicon: <c>valid</c>, or
    /// <c>unknown</c> when it does not know the Lexicon. <see langword="null"/> when not reported.
    /// </summary>
    public string? ValidationStatus { get; init; }

    /// <summary>
    /// A <c>com.atproto.repo.strongRef</c> to this version of the record, as a like, repost or
    /// reply references it.
    /// </summary>
    public StrongRef ToStrongRef() => new() { Uri = Uri, Cid = Cid };
}

/// <summary>
/// A record read from a repository: its AT URI, the CID of the version read, and its value.
/// </summary>
/// <typeparam name="T">The deserialized record type.</typeparam>
public sealed record RecordView<T>
{
    /// <summary>Creates a record view.</summary>
    /// <param name="uri">The record's AT URI; it must name a collection and a record key.</param>
    /// <param name="cid">The CID of the record version, when known.</param>
    /// <param name="value">The record value.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> does not name a record.</exception>
    public RecordView(AtUri uri, Cid? cid, T value)
    {
        ArgumentNullException.ThrowIfNull(uri);

        RecordKey = RecordPaths.RecordKeyOf(uri);
        Uri = uri;
        Cid = cid;
        Value = value;
    }

    /// <summary>The AT URI of the record.</summary>
    public AtUri Uri { get; }

    /// <summary>The CID (content hash) of the record version, when the service reported it.</summary>
    public Cid? Cid { get; }

    /// <summary>The deserialized record value.</summary>
    public T Value { get; }

    /// <summary>The record key portion of the URI.</summary>
    public RecordKey RecordKey { get; }
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

/// <summary>
/// What the record helpers share: splitting an AT URI into its record path, and the collection a
/// record type declares.
/// </summary>
internal static class RecordPaths
{
    /// <summary>The URI's record key, or <see cref="ArgumentException"/> when it names no record.</summary>
    public static RecordKey RecordKeyOf(AtUri uri) => PathOf(uri).Rkey;

    /// <summary>
    /// The URI's collection and record key, or <see cref="ArgumentException"/> when it names no
    /// record.
    /// </summary>
    public static (Nsid Collection, RecordKey Rkey) PathOf(AtUri uri, string? paramName = "uri")
    {
        ArgumentNullException.ThrowIfNull(uri, paramName);
        return uri is { Collection: { } collection, RecordKey: { } rkey }
            ? (collection, rkey)
            : throw new ArgumentException(
                $"'{uri}' does not name a record: it needs a collection and a record key.", paramName);
    }

    /// <summary>
    /// The collection <typeparamref name="T"/> declares through <see cref="IAtProtoRecord"/>, or
    /// <see langword="null"/> when it does not implement it.
    /// </summary>
    public static Nsid? DeclaredCollection<T>() => DeclaredCollectionCache<T>.Value;

    private static class DeclaredCollectionCache<T>
    {
        // A static abstract member can only be read through a type parameter constrained to the
        // interface; the interface map reaches it (implicit or explicit) from an unconstrained T.
        public static readonly Nsid? Value = Resolve();

        private static Nsid? Resolve()
        {
            if (!typeof(IAtProtoRecord).IsAssignableFrom(typeof(T)) || typeof(T).IsInterface)
                return null;

            var map = typeof(T).GetInterfaceMap(typeof(IAtProtoRecord));
            var getter = map.TargetMethods[Array.FindIndex(
                map.InterfaceMethods,
                m => m.Name == $"get_{nameof(IAtProtoRecord.Collection)}")];

            return (Nsid?)getter.Invoke(null, BindingFlags.DoNotWrapExceptions, binder: null, parameters: null, culture: null);
        }
    }
}
