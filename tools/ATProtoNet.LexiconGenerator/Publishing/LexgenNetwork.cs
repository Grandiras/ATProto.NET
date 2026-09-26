using System.Text.Json;
using System.Text.Json.Nodes;
using ATProtoNet.Http;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.Com.AtProto.Lexicon;

namespace ATProtoNet.LexiconGenerator.Publishing;

/// <summary>
/// The network operations of the <c>publish</c> and <c>resolve</c> commands, behind one seam so
/// the commands can be exercised without a network.
/// </summary>
internal interface ILexgenNetwork
{
    /// <summary>Resolves and verifies a published schema, from <paramref name="authority"/> when given, else through DNS.</summary>
    /// <exception cref="LexiconResolutionException">The schema could not be resolved.</exception>
    Task<ResolvedLexicon> ResolveAsync(Nsid nsid, Did? authority, CancellationToken cancellationToken);

    /// <summary>The DID the NSID's <c>_lexicon</c> record names, or <see langword="null"/> when there is none.</summary>
    /// <exception cref="LexiconResolutionException">The lookup itself failed.</exception>
    Task<Did?> ResolveAuthorityAsync(Nsid nsid, CancellationToken cancellationToken);

    /// <summary>Signs in to the account that publishes the schemas.</summary>
    /// <param name="identifier">A handle, DID or email address.</param>
    /// <param name="password">The password; an app password is best.</param>
    /// <param name="pds">The PDS to sign in at, or <see langword="null"/> to look it up from the identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ILexiconRepository> SignInAsync(string identifier, string password, Uri? pds, CancellationToken cancellationToken);
}

/// <summary>The repository schemas are published to, as the signed-in account.</summary>
internal interface ILexiconRepository : IAsyncDisposable
{
    /// <summary>The account's DID: the repository, and what the <c>_lexicon</c> records must name.</summary>
    Did Did { get; }

    /// <summary>The schema record currently published for an NSID, or <see langword="null"/>.</summary>
    Task<PublishedSchema?> GetAsync(Nsid nsid, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the schema record for an NSID (record key = NSID). With <paramref name="replacing"/>,
    /// it replaces that record only while it is still the current one (<c>putRecord</c> with
    /// <c>swapRecord</c>); without, it creates the record and fails if one appeared meanwhile
    /// (<c>createRecord</c>).
    /// </summary>
    Task<AtUri> PutAsync(Nsid nsid, JsonObject record, Cid? replacing, CancellationToken cancellationToken);
}

/// <summary>A schema record as it is currently published.</summary>
/// <param name="Value">The record.</param>
/// <param name="Cid">Its CID, when the PDS reported it.</param>
internal sealed record PublishedSchema(JsonElement Value, Cid? Cid);

/// <summary>The network, through the SDK: its identity and Lexicon resolvers, and <see cref="AtProtoClient"/>.</summary>
internal sealed class SdkLexgenNetwork : ILexgenNetwork, IDisposable
{
    /// <summary>
    /// Whether the account password may be sent to <paramref name="pds"/>: over HTTPS, or over
    /// plain HTTP only to a PDS on this machine (a local development server).
    /// </summary>
    internal static bool MaySignInAt(Uri pds) =>
        pds.Scheme == Uri.UriSchemeHttps || (pds.Scheme == Uri.UriSchemeHttp && pds.IsLoopback);

    private readonly IdentityResolver _identity = IdentityResolver.CreateDefault();
    private readonly LexiconResolver _lexicons;

    public SdkLexgenNetwork()
    {
        _lexicons = new LexiconResolver(_identity.DidResolver);
    }

    public Task<ResolvedLexicon> ResolveAsync(Nsid nsid, Did? authority, CancellationToken cancellationToken) =>
        authority is null
            ? _lexicons.ResolveAsync(nsid, cancellationToken)
            : _lexicons.ResolveAsync(nsid, authority, cancellationToken);

    public async Task<Did?> ResolveAuthorityAsync(Nsid nsid, CancellationToken cancellationToken)
    {
        try
        {
            return await _lexicons.ResolveAuthorityAsync(nsid, cancellationToken).ConfigureAwait(false);
        }
        catch (LexiconResolutionException ex) when (ex.Kind == LexiconResolutionErrorKind.AuthorityNotFound)
        {
            return null;
        }
    }

    public async Task<ILexiconRepository> SignInAsync(
        string identifier, string password, Uri? pds, CancellationToken cancellationToken)
    {
        if (pds is null)
        {
            if (!AtIdentifier.TryParse(identifier, out var account))
                throw new LexgenException($"'{identifier}' is not a handle or a DID; pass --pds to sign in with it.");

            var identity = await _identity.ResolveAsync(account, cancellationToken).ConfigureAwait(false);
            pds = identity.PdsEndpoint
                ?? throw new LexgenException($"The DID document of {identity.Did} publishes no PDS; pass --pds.");
            if (!MaySignInAt(pds))
                throw new LexgenException($"The PDS of {identity.Did} is {pds}, which is not HTTPS; the password is not sent to it.");
        }

        var client = new AtProtoClient(new AtProtoClientOptions { InstanceUrl = pds.ToString() });
        try
        {
            await client.LoginAsync(identifier, password, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new ClientRepository(client);
    }

    public void Dispose()
    {
        _lexicons.Dispose();
        _identity.Dispose();
    }

    private sealed class ClientRepository(AtProtoClient client) : ILexiconRepository
    {
        public Did Did => client.Did!;

        public async Task<PublishedSchema?> GetAsync(Nsid nsid, CancellationToken cancellationToken)
        {
            try
            {
                var view = await client.Repo.GetRecordAsync(
                    Did, LexiconSchemaRecord.Collection, RecordKey.Parse(nsid.Value), cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                return new PublishedSchema(view.Value, view.Cid);
            }
            catch (XrpcException ex) when (ex.Is(XrpcErrors.RecordNotFound))
            {
                return null;
            }
        }

        public async Task<AtUri> PutAsync(Nsid nsid, JsonObject record, Cid? replacing, CancellationToken cancellationToken)
        {
            var rkey = RecordKey.Parse(nsid.Value);
            var written = replacing is null
                ? await client.Repo.CreateRecordAsync(
                    Did, LexiconSchemaRecord.Collection, record, rkey, cancellationToken: cancellationToken)
                    .ConfigureAwait(false)
                : await client.Repo.PutRecordAsync(
                    Did, LexiconSchemaRecord.Collection, rkey, record,
                    swapRecord: replacing, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            return written.Uri;
        }

        public async ValueTask DisposeAsync()
        {
            // The session was only for this run; end it rather than leave it valid on the server.
            try
            {
                await client.LogoutAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is AtProtoException or HttpRequestException)
            {
                // Best effort: the session expires on its own.
            }

            await client.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>A command failure with a message for the user, reported without a stack trace.</summary>
internal sealed class LexgenException(string message) : Exception(message);
