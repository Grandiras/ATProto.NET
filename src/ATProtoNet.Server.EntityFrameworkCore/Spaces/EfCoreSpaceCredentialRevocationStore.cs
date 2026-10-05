using ATProtoNet.Server.Authentication;
using ATProtoNet.Server.Spaces;
using ATProtoNet.Spaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="ISpaceCredentialRevocationStore"/>: revoked space credentials in a relational
/// table shared by every instance.
/// </summary>
/// <remarks>
/// <para>A revocation is keyed on <c>(space, jti)</c> and carries the instant it must be kept until. Recording one
/// raises an existing row's retention with a conditional update, never lowers it, and inserts the rows that are
/// missing; two instances told of the same credential at once both succeed.</para>
/// <para>Rows past their retention are swept at most once a minute, in the background, and only those: a row
/// whose retention has not passed is a credential still refused. A sweep that fails is only logged, since
/// <see cref="IsRevokedAsync"/> ignores rows past their retention on its own.</para>
/// <para>Register it with <see cref="SpaceStoreExtensions.AddAtProtoEfCoreSpaceCredentialRevocationStore{TContext}"/>.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> carrying <see cref="SpaceCredentialRevocationEntity"/>. Use <see cref="SpaceDbContext"/>,
/// or your own context configured with <see cref="SpaceDbContext.ConfigureSpaceCredentialRevocationModel"/>.
/// </typeparam>
public sealed class EfCoreSpaceCredentialRevocationStore<TContext> : ISpaceCredentialRevocationStore
    where TContext : DbContext
{
    // Bounds the IN list of one statement, below every provider's parameter limit.
    private const int BatchSize = 100;

    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly SweepSchedule _sweeps;
    private readonly ILogger _logger;

    /// <summary>Creates the store.</summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="logger">Receives sweep diagnostics.</param>
    public EfCoreSpaceCredentialRevocationStore(
        IDbContextFactory<TContext> contextFactory,
        ILogger<EfCoreSpaceCredentialRevocationStore<TContext>>? logger = null)
        : this(contextFactory, TimeProvider.System, logger)
    {
    }

    /// <summary>Creates the store, scheduling its sweep against <paramref name="timeProvider"/>.</summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="timeProvider">The clock the sweep is scheduled against.</param>
    /// <param name="logger">Receives sweep diagnostics.</param>
    public EfCoreSpaceCredentialRevocationStore(
        IDbContextFactory<TContext> contextFactory,
        TimeProvider timeProvider,
        ILogger<EfCoreSpaceCredentialRevocationStore<TContext>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _contextFactory = contextFactory;
        _sweeps = new SweepSchedule(timeProvider);
        _logger = logger ?? (ILogger)NullLogger.Instance;
    }

    // The most recent background sweep, for tests to wait on.
    internal Task LastSweep => _sweeps.Last;

    /// <inheritdoc/>
    public async Task RevokeAsync(
        SpaceUri space, IReadOnlyCollection<string> credentialIds, DateTimeOffset retainUntil, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);
        ArgumentNullException.ThrowIfNull(credentialIds);

        var spaceKey = space.ToString();
        var until = retainUntil.ToUnixTimeMilliseconds();

        foreach (var batch in credentialIds.Distinct(StringComparer.Ordinal).Chunk(BatchSize))
            await RevokeBatchAsync(spaceKey, batch, until, cancellationToken).ConfigureAwait(false);

        _sweeps.RunIfDue(now => SweepAsync(now.ToUnixTimeMilliseconds()));
    }

    private async Task RevokeBatchAsync(string space, string[] ids, long until, CancellationToken cancellationToken)
    {
        // A concurrent instance may insert the same row between the read and the save; the retry then finds it
        // and raises it instead.
        for (var attempt = 1; ; attempt++)
        {
            var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
            await using var scope = context.ConfigureAwait(false);
            var rows = context.Set<SpaceCredentialRevocationEntity>();

            await rows.Where(e => e.Space == space && ids.Contains(e.CredentialId) && e.RetainUntil < until)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.RetainUntil, until), cancellationToken).ConfigureAwait(false);

            var known = await rows.Where(e => e.Space == space && ids.Contains(e.CredentialId))
                .Select(e => e.CredentialId).ToListAsync(cancellationToken).ConfigureAwait(false);

            foreach (var id in ids.Except(known, StringComparer.Ordinal))
                rows.Add(new SpaceCredentialRevocationEntity { Space = space, CredentialId = id, RetainUntil = until });

            try
            {
                await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask<bool> IsRevokedAsync(
        SpaceUri space, string credentialId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(space);

        var spaceKey = space.ToString();
        var cutoff = now.ToUnixTimeMilliseconds();

        return await _contextFactory.UseAsync((context, ct) => context.Set<SpaceCredentialRevocationEntity>()
            .AsNoTracking()
            .AnyAsync(e => e.Space == spaceKey && e.CredentialId == credentialId && e.RetainUntil > cutoff, ct), cancellationToken).ConfigureAwait(false);
    }

    private async Task SweepAsync(long cutoff)
    {
        try
        {
            await _contextFactory.UseAsync((context, ct) => context.Set<SpaceCredentialRevocationEntity>()
                .Where(e => e.RetainUntil <= cutoff)
                .ExecuteDeleteAsync(ct), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Housekeeping only: a row past its retention is ignored by IsRevokedAsync, so a table that keeps
            // growing is a storage problem, never a correctness one.
            _logger.LogWarning(ex, "Sweeping expired credential revocations failed; they remain until the next sweep");
        }
    }
}
