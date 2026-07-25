namespace ATProtoNet.Admin;

/// <summary>
/// Configuration for a <see cref="PdsAdminClient"/>.
/// </summary>
public sealed class PdsAdminOptions
{
    /// <summary>
    /// The base URL of the PDS to administer (e.g. <c>https://pds.example.com</c>).
    /// Must be HTTPS unless it points at a loopback address.
    /// </summary>
    public required string Url { get; set; }

    /// <summary>
    /// The PDS admin password — the server's <c>PDS_ADMIN_PASSWORD</c>.
    /// </summary>
    public required string AdminPassword { get; set; }

    /// <summary>
    /// The admin user name used for HTTP Basic authentication.
    /// Default: <c>"admin"</c>, which is what the reference PDS expects.
    /// </summary>
    public string AdminUser { get; set; } = "admin";

    /// <summary>
    /// Permits sending the admin password over plaintext HTTP to a non-loopback host.
    /// Default: <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set this only when the PDS is reached over a network you trust — typically a
    /// container or private network inside one deployment, where the PDS container
    /// serves plaintext HTTP and nothing untrusted can observe the hop. Anywhere the
    /// traffic crosses a boundary you do not control, put TLS in front of the PDS
    /// instead: the admin password grants full control of every account on the server.
    /// </para>
    /// <para>
    /// Aspire AppHosts set this automatically in run mode, where the PDS container and
    /// its consumer share a local network. Published deployments must opt in
    /// deliberately.
    /// </para>
    /// </remarks>
    public bool AllowInsecureHttp { get; set; }
}

/// <summary>
/// Describes an account to provision on a PDS via
/// <see cref="PdsAdminClient.CreateAccountAsync"/>.
/// </summary>
public sealed class CreatePdsAccountRequest
{
    /// <summary>
    /// The handle for the new account (e.g. <c>alice.pds.example.com</c>).
    /// It must fall under one of the PDS's available user domains — see
    /// <see cref="PdsAdminClient.DescribeServerAsync"/>.
    /// </summary>
    public required string Handle { get; init; }

    /// <summary>
    /// The account password.
    /// </summary>
    public required string Password { get; init; }

    /// <summary>
    /// The account's email address. Required by the reference PDS.
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// An invite code to consume. When <c>null</c> and the server requires invites,
    /// <see cref="PdsAdminClient.CreateAccountAsync"/> mints a single-use code
    /// with the admin credentials first.
    /// </summary>
    public string? InviteCode { get; init; }

    /// <summary>
    /// A pre-existing DID to bind the account to. When <c>null</c>, the PDS
    /// creates a new <c>did:plc</c> identity.
    /// </summary>
    public string? Did { get; init; }

    /// <summary>
    /// An optional <c>did:key</c> to register as a PLC rotation key, letting the
    /// account holder migrate away from this PDS later.
    /// </summary>
    public string? RecoveryKey { get; init; }
}
