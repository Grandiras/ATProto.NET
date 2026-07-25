using System.Text.Json;
using ATProtoNet.Http;
using ATProtoNet.Lexicon.Com.AtProto.Admin;
using ATProtoNet.Lexicon.Com.AtProto.Server;
using ATProtoNet.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Admin;

/// <summary>
/// Administrative client for a PDS you operate — the counterpart to
/// <see cref="AtProtoClient"/>, which acts on behalf of a user.
/// </summary>
/// <remarks>
/// <para>
/// Authenticates with the server's admin password over HTTP Basic, the scheme the
/// reference Bluesky PDS (<c>ghcr.io/bluesky-social/pds</c>) expects on
/// <c>com.atproto.admin.*</c>. Pair it with the <c>ATProtoNet.Aspire.Hosting</c>
/// package to run that container locally, or point it at any PDS you hold the
/// admin password for.
/// </para>
/// <para>
/// The primary use case is letting your application create accounts on its own
/// managed PDS instead of relying on an external provider —
/// <see cref="CreateAccountAsync"/> handles invite codes for you.
/// </para>
/// <example>
/// <code>
/// using var admin = new PdsAdminClient("https://pds.example.com", adminPassword);
///
/// var account = await admin.CreateAccountAsync(new CreatePdsAccountRequest
/// {
///     Handle = "alice.pds.example.com",
///     Email = "alice@example.com",
///     Password = signupPassword,
/// });
///
/// Console.WriteLine(account.Did); // did:plc:...
/// </code>
/// </example>
/// </remarks>
public sealed class PdsAdminClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly XrpcClient _adminXrpc;
    private readonly XrpcClient _publicXrpc;
    private readonly ILogger _logger;
    private bool _disposed;

    /// <summary>
    /// Create an admin client for the given PDS.
    /// </summary>
    /// <param name="pdsUrl">The PDS base URL. Must be HTTPS unless it is a loopback address.</param>
    /// <param name="adminPassword">The server's admin password.</param>
    public PdsAdminClient(string pdsUrl, string adminPassword)
        : this(new PdsAdminOptions { Url = pdsUrl, AdminPassword = adminPassword }, null, null)
    {
    }

    /// <summary>
    /// Create an admin client with full configuration.
    /// </summary>
    /// <param name="options">Connection and credential options.</param>
    /// <param name="httpClient">
    /// An externally managed <see cref="HttpClient"/> (e.g. from <c>IHttpClientFactory</c>).
    /// When <c>null</c>, the client creates and owns one.
    /// </param>
    /// <param name="logger">An optional logger.</param>
    /// <exception cref="ArgumentException">
    /// Thrown when the effective base address is neither HTTPS nor a loopback address and
    /// <see cref="PdsAdminOptions.AllowInsecureHttp"/> is not set — sending the admin
    /// password over plaintext HTTP would expose it.
    /// </exception>
    public PdsAdminClient(
        PdsAdminOptions options,
        HttpClient? httpClient,
        ILogger<PdsAdminClient>? logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Url);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.AdminPassword);

        _logger = logger ?? NullLogger<PdsAdminClient>.Instance;

        var baseUri = new Uri(options.Url.TrimEnd('/') + "/");

        if (httpClient is not null)
        {
            _httpClient = httpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient();
            _ownsHttpClient = true;
        }

        _httpClient.BaseAddress ??= baseUri;
        _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(
            $"ATProtoNet/{typeof(PdsAdminClient).Assembly.GetName().Version}");

        PdsUrl = _httpClient.BaseAddress!;

        if (PdsUrl != baseUri)
        {
            // A supplied HttpClient's own address wins, so the client would administer a
            // different server than the configuration names.
            _logger.LogWarning(
                "PDS admin client is using the supplied HttpClient's base address {ActualUrl}, " +
                "not the configured {ConfiguredUrl}",
                PdsUrl,
                baseUri);
        }

        // Validate the address requests actually go to, not the configured one: a supplied
        // HttpClient may already carry a different BaseAddress, and that is where the
        // Authorization header would be sent.
        if (!string.Equals(PdsUrl.Scheme, "https", StringComparison.OrdinalIgnoreCase)
            && !PdsUrl.IsLoopback
            && !options.AllowInsecureHttp)
        {
            throw new ArgumentException(
                $"Refusing to send the PDS admin password in the clear to '{PdsUrl}'. " +
                "Use HTTPS, or set PdsAdminOptions.AllowInsecureHttp " +
                "(configuration key 'AtProto:Pds:AllowInsecureHttp') if the PDS is only " +
                "reachable over a private network you trust.",
                nameof(options));
        }

        // Two XRPC clients over one HttpClient: admin endpoints need the Basic header,
        // while createAccount is an ordinary unauthenticated signup call.
        _adminXrpc = new XrpcClient(_httpClient, _logger, AtProtoJsonDefaults.Options);
        _adminXrpc.SetAdminCredentials(options.AdminPassword, options.AdminUser);
        _publicXrpc = new XrpcClient(_httpClient, _logger, AtProtoJsonDefaults.Options);

        Admin = new AdminClient(_adminXrpc, _logger);
        Server = new ServerClient(_adminXrpc, _logger);
    }

    /// <summary>
    /// The base URL of the PDS being administered.
    /// </summary>
    public Uri PdsUrl { get; }

    /// <summary>
    /// The raw <c>com.atproto.admin.*</c> client, authenticated with the admin password.
    /// Use it for endpoints this class does not wrap.
    /// </summary>
    public AdminClient Admin { get; }

    /// <summary>
    /// The raw <c>com.atproto.server.*</c> client, authenticated with the admin password.
    /// Use it for endpoints this class does not wrap.
    /// </summary>
    public ServerClient Server { get; }

    // ──────────────────────────────────────────────────────────
    //  Server
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Describe the PDS — its DID, available user domains, and whether signups
    /// require an invite code.
    /// </summary>
    public Task<DescribeServerResponse> DescribeServerAsync(CancellationToken cancellationToken = default) =>
        _publicXrpc.QueryAsync<DescribeServerResponse>(
            "com.atproto.server.describeServer", null, cancellationToken);

    // ──────────────────────────────────────────────────────────
    //  Invite codes
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Mint an invite code.
    /// </summary>
    /// <param name="useCount">How many accounts the code may create. Default: 1.</param>
    /// <param name="forAccount">An optional DID to attribute the code to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The generated invite code.</returns>
    public async Task<string> CreateInviteCodeAsync(
        int useCount = 1,
        string? forAccount = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(useCount, 1);

        var response = await Server.CreateInviteCodeAsync(useCount, forAccount, cancellationToken);
        return response.Code;
    }

    /// <summary>
    /// Mint several invite codes in one call.
    /// </summary>
    /// <param name="codeCount">How many codes to generate.</param>
    /// <param name="useCount">How many accounts each code may create. Default: 1.</param>
    /// <param name="forAccounts">Optional DIDs to attribute the codes to.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public async Task<IReadOnlyList<string>> CreateInviteCodesAsync(
        int codeCount,
        int useCount = 1,
        IEnumerable<string>? forAccounts = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(codeCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(useCount, 1);

        var response = await Server.CreateInviteCodesAsync(
            new CreateInviteCodesRequest
            {
                CodeCount = codeCount,
                UseCount = useCount,
                ForAccounts = forAccounts?.ToList(),
            },
            cancellationToken);

        return response.Codes.SelectMany(c => c.Codes).ToList();
    }

    // ──────────────────────────────────────────────────────────
    //  Accounts
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create an account on the PDS, minting an invite code first when the server
    /// requires one and the caller did not supply it.
    /// </summary>
    /// <param name="request">The account to create.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>
    /// The new account's DID and handle, along with a session (access and refresh
    /// tokens) the account holder can be signed in with immediately.
    /// </returns>
    public async Task<CreateAccountResponse> CreateAccountAsync(
        CreatePdsAccountRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Handle);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Password);

        var inviteCode = request.InviteCode;

        if (inviteCode is null)
        {
            var server = await DescribeServerAsync(cancellationToken);

            if (server.InviteCodeRequired == true)
            {
                inviteCode = await CreateInviteCodeAsync(cancellationToken: cancellationToken);
                _logger.LogDebug("Minted invite code for new account {Handle}", request.Handle);
            }
        }

        // Signup is an ordinary public endpoint — send it without the admin header.
        return await _publicXrpc.ProcedureAsync<CreateAccountRequest, CreateAccountResponse>(
            "com.atproto.server.createAccount",
            new CreateAccountRequest
            {
                Handle = request.Handle,
                Password = request.Password,
                Email = request.Email,
                Did = request.Did,
                InviteCode = inviteCode,
                RecoveryKey = request.RecoveryKey,
            },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get detailed information about an account.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task<AccountInfo> GetAccountAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        return Admin.GetAccountInfoAsync(did, cancellationToken);
    }

    /// <summary>
    /// Permanently delete an account and its repository.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task DeleteAccountAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        return Admin.DeleteAccountAsync(did, cancellationToken);
    }

    /// <summary>
    /// Take an account down, making its content unavailable.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="reference">An optional moderation reference recorded with the takedown.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task TakedownAccountAsync(
        string did,
        string? reference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        return Admin.UpdateSubjectStatusAsync(
            new UpdateSubjectStatusRequest
            {
                Subject = CreateRepoRef(did),
                Takedown = new SubjectStatusDetail { Applied = true, Ref = reference },
            },
            cancellationToken);
    }

    /// <summary>
    /// Reverse a takedown, restoring the account's content.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task RestoreAccountAsync(string did, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);

        return Admin.UpdateSubjectStatusAsync(
            new UpdateSubjectStatusRequest
            {
                Subject = CreateRepoRef(did),
                Takedown = new SubjectStatusDetail { Applied = false },
            },
            cancellationToken);
    }

    /// <summary>
    /// Change an account's handle.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="handle">The new handle.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task UpdateAccountHandleAsync(
        string did,
        string handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(handle);
        return Admin.UpdateAccountHandleAsync(did, handle, cancellationToken);
    }

    /// <summary>
    /// Change an account's email address.
    /// </summary>
    /// <param name="account">The account DID or handle.</param>
    /// <param name="email">The new email address.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task UpdateAccountEmailAsync(
        string account,
        string email,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(account);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        return Admin.UpdateAccountEmailAsync(account, email, cancellationToken);
    }

    /// <summary>
    /// Reset an account's password.
    /// </summary>
    /// <param name="did">The account DID.</param>
    /// <param name="password">The new password.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task UpdateAccountPasswordAsync(
        string did,
        string password,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(did);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        return Admin.UpdateAccountPasswordAsync(did, password, cancellationToken);
    }

    // ──────────────────────────────────────────────────────────
    //  Clients for the accounts this PDS hosts
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// Create an <see cref="AtProtoClient"/> pointed at this PDS, for acting on
    /// behalf of one of its accounts.
    /// </summary>
    /// <remarks>
    /// The returned client is unauthenticated — call
    /// <see cref="AtProtoClient.LoginAsync"/> or
    /// <see cref="AtProtoClient.ResumeSessionAsync"/> on it. It owns its own
    /// <see cref="HttpClient"/> and is not disposed by this instance.
    /// </remarks>
    public AtProtoClient CreateClient() =>
        new(new AtProtoClientOptions { InstanceUrl = PdsUrl.ToString().TrimEnd('/') });

    private static JsonElement CreateRepoRef(string did) =>
        JsonSerializer.SerializeToElement(
            new Dictionary<string, string>
            {
                ["$type"] = "com.atproto.admin.defs#repoRef",
                ["did"] = did,
            },
            AtProtoJsonDefaults.Options);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _adminXrpc.Dispose();
        _publicXrpc.Dispose();

        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
