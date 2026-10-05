using ATProtoNet.Server.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="IJtiReplayStore"/>: single-use token identifiers in a relational
/// table shared by every instance.
/// </summary>
/// <remarks>
/// <para>Consuming a token is one insert. The table's primary key is <c>(iss, jti, exp)</c>, what
/// the store is keyed on, so the database's uniqueness enforcement <em>is</em> the replay check:
/// two instances presented the same token concurrently see exactly one success between
/// them.</para>
/// <para>Expired rows are swept at most once a minute, in the background. A token past its expiry
/// is refused on the expiry itself, so a sweep that fails is only logged.</para>
/// <para>Register it with
/// <see cref="JtiReplayStoreExtensions.AddAtProtoEfCoreJtiReplayStore{TContext}"/>.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> carrying <see cref="JtiReplayEntity"/>. Use
/// <see cref="JtiReplayDbContext"/> or <see cref="SpaceDbContext"/>, or your own context configured
/// with <see cref="JtiReplayDbContext.ConfigureJtiReplayModel"/>.
/// </typeparam>
public sealed class EfCoreJtiReplayStore<TContext> : IJtiReplayStore
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly SweepSchedule _sweeps;
    private readonly ILogger _logger;

    /// <summary>Creates the store.</summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="logger">Receives sweep diagnostics.</param>
    public EfCoreJtiReplayStore(
        IDbContextFactory<TContext> contextFactory,
        ILogger<EfCoreJtiReplayStore<TContext>>? logger = null)
        : this(contextFactory, TimeProvider.System, logger)
    {
    }

    /// <summary>Creates the store, reading the current time from <paramref name="timeProvider"/>.</summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="timeProvider">The clock the sweep is scheduled against.</param>
    /// <param name="logger">Receives sweep diagnostics.</param>
    /// <remarks>
    /// This constructor's parameters are a superset of the other's on purpose: the container
    /// picks it only when a <see cref="TimeProvider"/> is registered, and never has two
    /// constructors it cannot choose between.
    /// </remarks>
    public EfCoreJtiReplayStore(
        IDbContextFactory<TContext> contextFactory,
        TimeProvider timeProvider,
        ILogger<EfCoreJtiReplayStore<TContext>>? logger = null)
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
    public async ValueTask<bool> TryConsumeAsync(
        string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        var expiry = expiresAt.ToUnixTimeSeconds();

        var context = await _contextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var contextScope = context.ConfigureAwait(false);
        context.Add(new JtiReplayEntity { Issuer = issuer, TokenId = tokenId, ExpiresAt = expiry });

        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Rows hold whole seconds, rounded down from the instant the entry must outlive, so a
            // row stamped with this very second may still guard a token for a fraction of it: only
            // rows from earlier seconds are certainly past.
            _sweeps.RunIfDue(now => SweepAsync(now.ToUnixTimeSeconds()));
            return true;
        }
        catch (DbUpdateException)
        {
            // The insert is the check, but a key violation is not the only thing that can fail a
            // save. Confirm the identifier really is spent before calling it a replay, so a
            // genuine storage fault surfaces as an error rather than as a valid token being
            // silently refused.
            context.ChangeTracker.Clear();

            var spent = await context.Set<JtiReplayEntity>()
                .AsNoTracking()
                .AnyAsync(
                    e => e.Issuer == issuer && e.TokenId == tokenId && e.ExpiresAt == expiry,
                    cancellationToken).ConfigureAwait(false);

            if (!spent)
                throw;

            return false;
        }
    }

    private async Task SweepAsync(long cutoff)
    {
        try
        {
            await _contextFactory.UseAsync((context, ct) => context.Set<JtiReplayEntity>()
                .Where(e => e.ExpiresAt < cutoff)
                .ExecuteDeleteAsync(ct), CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Housekeeping only: a token past its expiry is rejected on the expiry itself, so a
            // table that keeps growing is a storage problem, never a correctness one.
            _logger.LogWarning(ex, "Sweeping expired replay entries failed; they remain until the next sweep");
        }
    }
}
