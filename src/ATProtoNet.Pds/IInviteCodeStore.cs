namespace ATProtoNet.Pds;

/// <summary>
/// A single redemption of an invite code.
/// </summary>
public sealed class PdsInviteCodeUse
{
    /// <summary>The DID of the account that redeemed the code.</summary>
    public required string UsedBy { get; init; }

    /// <summary>When the code was redeemed.</summary>
    public DateTimeOffset UsedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// An invite code stored in the PDS.
/// </summary>
/// <remarks>
/// <see cref="ClaimedUses"/> counts <em>reservations</em>, not completed sign-ups: it is
/// incremented by <see cref="IInviteCodeStore.TryClaimAsync"/> before the account is created
/// and decremented again by <see cref="IInviteCodeStore.ReleaseClaimAsync"/> if creation fails.
/// <see cref="Uses"/> only ever contains confirmed redemptions, so
/// <c>ClaimedUses &gt;= Uses.Count</c> holds while a sign-up is in flight.
/// </remarks>
public sealed class PdsInviteCode
{
    /// <summary>The code itself (e.g. <c>my-pds-example-com-a2b3c-d4e5f</c>).</summary>
    public required string Code { get; init; }

    /// <summary>How many times the code may be redeemed in total. Default: 1 (single-use).</summary>
    public int AvailableUses { get; init; } = 1;

    /// <summary>How many uses are currently claimed (reserved or confirmed).</summary>
    public int ClaimedUses { get; set; }

    /// <summary>Whether the code has been disabled by an admin.</summary>
    public bool Disabled { get; set; }

    /// <summary>The DID this code belongs to, or <c>null</c> for admin-issued codes.</summary>
    public string? ForAccount { get; init; }

    /// <summary>Who created the code — a DID, or "admin" for admin-issued codes.</summary>
    public required string CreatedBy { get; init; }

    /// <summary>When the code was created.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>The confirmed redemptions of this code.</summary>
    public List<PdsInviteCodeUse> Uses { get; init; } = [];

    /// <summary>How many uses remain (never negative).</summary>
    public int RemainingUses => Math.Max(0, AvailableUses - ClaimedUses);
}

/// <summary>
/// Sort order for <see cref="InviteCodeQuery"/>.
/// </summary>
public enum InviteCodeSort
{
    /// <summary>Newest codes first (<c>sort=recent</c>).</summary>
    Recent = 0,

    /// <summary>Most-used codes first (<c>sort=usage</c>).</summary>
    Usage = 1,
}

/// <summary>
/// Query parameters for <see cref="IInviteCodeStore.ListAsync"/>.
/// </summary>
public sealed class InviteCodeQuery
{
    /// <summary>Only return codes belonging to this DID. <c>null</c> returns every code.</summary>
    public string? ForAccount { get; init; }

    /// <summary>Maximum number of codes to return. Default: 100.</summary>
    public int Limit { get; init; } = 100;

    /// <summary>Opaque continuation token from a previous page.</summary>
    public string? Cursor { get; init; }

    /// <summary>Sort order. Default: <see cref="InviteCodeSort.Recent"/>.</summary>
    public InviteCodeSort Sort { get; init; } = InviteCodeSort.Recent;
}

/// <summary>
/// A page of invite codes.
/// </summary>
public sealed class InviteCodePage
{
    /// <summary>The codes in this page.</summary>
    public required IReadOnlyList<PdsInviteCode> Codes { get; init; }

    /// <summary>Opaque cursor for the next page, or <c>null</c> when this is the last page.</summary>
    public string? Cursor { get; init; }
}

/// <summary>
/// Persistent store for PDS invite codes. Consulted by
/// <see cref="PdsService.CreateAccountAsync"/> whenever
/// <see cref="PdsOptions.OpenRegistration"/> is <c>false</c>.
/// </summary>
/// <remarks>
/// Redemption is a three-step protocol so concurrent sign-ups cannot double-spend a code:
/// <list type="number">
/// <item><see cref="TryClaimAsync"/> atomically reserves one use (a database implementation
/// should do this in a <em>single</em> conditional <c>UPDATE</c>, not a read-then-write).</item>
/// <item><see cref="ConfirmClaimAsync"/> records who redeemed it once the account exists.</item>
/// <item><see cref="ReleaseClaimAsync"/> returns the reservation if account creation failed.</item>
/// </list>
/// </remarks>
public interface IInviteCodeStore
{
    /// <summary>Store a newly created invite code.</summary>
    /// <exception cref="InvalidOperationException">The code already exists.</exception>
    Task CreateAsync(PdsInviteCode code, CancellationToken cancellationToken = default);

    /// <summary>Get a code by its value, or <c>null</c> if it does not exist.</summary>
    Task<PdsInviteCode?> GetAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically reserve one use of a code. Returns <c>false</c> when the code does not
    /// exist, is disabled, or has no uses left — implementations must make this a single
    /// atomic operation so two concurrent sign-ups cannot both claim the last use.
    /// </summary>
    Task<bool> TryClaimAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Record a confirmed redemption against a previously claimed code.
    /// </summary>
    /// <param name="code">The claimed code.</param>
    /// <param name="usedByDid">The DID of the account that was created.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ConfirmClaimAsync(string code, string usedByDid, CancellationToken cancellationToken = default);

    /// <summary>
    /// Return a reservation taken by <see cref="TryClaimAsync"/> that was never confirmed,
    /// making the use available again.
    /// </summary>
    Task ReleaseClaimAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>List invite codes, newest or most-used first, with cursor pagination.</summary>
    Task<InviteCodePage> ListAsync(InviteCodeQuery query, CancellationToken cancellationToken = default);

    /// <summary>Disable the given codes. Returns how many existing codes were disabled.</summary>
    Task<int> DisableAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disable every code belonging to the given accounts. Returns how many codes were disabled.
    /// </summary>
    Task<int> DisableForAccountsAsync(IEnumerable<string> accountDids, CancellationToken cancellationToken = default);
}
