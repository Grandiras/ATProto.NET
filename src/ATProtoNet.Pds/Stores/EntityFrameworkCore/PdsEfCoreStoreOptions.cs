namespace ATProtoNet.Pds.EntityFrameworkCore;

/// <summary>
/// Options for the EF Core-backed PDS stores.
/// </summary>
public sealed class PdsEfCoreStoreOptions
{
    /// <summary>
    /// Resolve accounts by handle and by email in memory instead of in SQL.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>Set this to <see langword="true"/> when the handle or email columns are
    /// encrypted at rest with a <b>non-deterministic</b> scheme (an EF Core value
    /// converter that encrypts on write, SQL Server Always Encrypted with randomized
    /// encryption, a provider extension). The same plaintext then produces different
    /// ciphertext on every write, so a <c>WHERE handle = @p</c> predicate can never
    /// match and <see cref="IAccountStore.GetByHandleAsync"/> would always return
    /// <see langword="null"/>.</para>
    /// <para>With this enabled, <see cref="EfCoreAccountStore{TContext}"/> streams
    /// accounts out of the database (decrypting them through the configured converter)
    /// and compares in memory. That is O(accounts) per lookup and reads every row — use
    /// it only when the encryption scheme leaves no other option, and see
    /// <see cref="MaxClientSideLookupRows"/> to bound the scan.</para>
    /// </remarks>
    public bool ClientSideAccountLookup { get; set; }

    /// <summary>
    /// Maximum number of account rows a client-side lookup will scan before giving up,
    /// or <see langword="null"/> (the default) for no limit. Ignored unless
    /// <see cref="ClientSideAccountLookup"/> is enabled.
    /// </summary>
    /// <remarks>
    /// A safety valve for deployments that outgrow the client-side scan: rather than
    /// pulling an unbounded table into memory on every login, the lookup stops after this
    /// many rows and reports "not found". Leave it <see langword="null"/> unless you would
    /// rather fail a lookup than exhaust memory.
    /// </remarks>
    public int? MaxClientSideLookupRows { get; set; }
}
