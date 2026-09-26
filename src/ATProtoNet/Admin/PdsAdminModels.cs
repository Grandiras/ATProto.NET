using ATProtoNet.Identity;

namespace ATProtoNet.Admin;

/// <summary>
/// How a <see cref="PdsAdminClient"/> proves it is allowed to call
/// <c>com.atproto.admin.*</c>. Which one applies is a property of the PDS
/// implementation, not a preference.
/// </summary>
public enum PdsAdminAuthentication
{
    /// <summary>
    /// HTTP Basic with a server-wide admin password, the scheme the reference Bluesky
    /// PDS (<c>ghcr.io/bluesky-social/pds</c>) expects. There is no account behind it;
    /// the password <em>is</em> the authority.
    /// </summary>
    AdminPassword = 0,

    /// <summary>
    /// A session belonging to an account the server has flagged as an administrator,
    /// which is how <see href="https://tangled.org/tranquil.farm/tranquil-pds">Tranquil
    /// PDS</see> works. The client signs in with
    /// <c>com.atproto.server.createSession</c> and sends the resulting bearer token.
    /// </summary>
    AdminAccount = 1,
}

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
    /// The password the client authenticates with.
    /// </summary>
    /// <remarks>
    /// Under <see cref="PdsAdminAuthentication.AdminPassword"/> this is the server's
    /// <c>PDS_ADMIN_PASSWORD</c>. Under
    /// <see cref="PdsAdminAuthentication.AdminAccount"/> it is the password of the
    /// account named by <see cref="AdminIdentifier"/>.
    /// </remarks>
    public required string AdminPassword { get; set; }

    /// <summary>
    /// Which authentication scheme the PDS expects on its admin endpoints.
    /// Default: <see cref="PdsAdminAuthentication.AdminPassword"/>.
    /// </summary>
    public PdsAdminAuthentication Authentication { get; set; } = PdsAdminAuthentication.AdminPassword;

    /// <summary>
    /// The handle or DID of the administrator account, for
    /// <see cref="PdsAdminAuthentication.AdminAccount"/>. Ignored otherwise, and
    /// required when that mode is selected.
    /// </summary>
    public string? AdminIdentifier { get; set; }

    /// <summary>
    /// The admin user name used for HTTP Basic authentication.
    /// Default: <c>"admin"</c>, which is what the reference PDS expects.
    /// Ignored under <see cref="PdsAdminAuthentication.AdminAccount"/>.
    /// </summary>
    public string AdminUser { get; set; } = "admin";

    /// <summary>
    /// Permits sending the admin credentials over plaintext HTTP to a non-loopback host.
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
