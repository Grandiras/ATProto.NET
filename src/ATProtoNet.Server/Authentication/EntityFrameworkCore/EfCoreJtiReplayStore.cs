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
/// <para>Consuming a token is one insert. The table's primary key is <c>(iss, jti, exp)</c> —
/// exactly what the store is keyed on — so the database's uniqueness enforcement <em>is</em> the
/// replay check, with no read-modify-write to race. Two instances presented the same token
/// concurrently therefore see exactly one success between them, which is the guarantee
/// <see cref="InMemoryJtiReplayStore"/> cannot give across a load balancer.</para>
/// <para>Expired rows are swept at most once a minute, in the background: the consumption that
/// finds a sweep due starts it and returns without waiting. Nothing depends on it for
/// correctness — a token past its expiry is rejected on the expiry itself, so a row that
/// outlives its sweep costs space and nothing else. A sweep that fails — a provider that cannot
/// translate the bulk delete, a transient outage — is logged and ignored.</para>
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
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private long _sweepDue;
    private Task _sweep = Task.CompletedTask;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="logger">Receives sweep diagnostics.</param>
    public EfCoreJtiReplayStore(
        IDbContextFactory<TContext> contextFactory,
        ILogger<EfCoreJtiReplayStore<TContext>>? logger = null)
        : this(contextFactory, TimeProvider.System, logger)
    {
    }

    /// <summary>
    /// Creates the store, reading the current time from <paramref name="timeProvider"/>.
    /// </summary>
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
        _timeProvider = timeProvider;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _sweepDue = _timeProvider.GetUtcNow().Add(SweepInterval).ToUnixTimeMilliseconds();
    }

    /// <summary>The most recent background sweep, for tests to wait on.</summary>
    internal Task LastSweep => Volatile.Read(ref _sweep);

    /// <inheritdoc/>
    public async ValueTask<bool> TryConsumeAsync(
        string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        var expiry = expiresAt.ToUnixTimeSeconds();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Add(new JtiReplayEntity { Issuer = issuer, TokenId = tokenId, ExpiresAt = expiry });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            SweepIfDue();
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
                    cancellationToken);

            if (!spent)
                throw;

            return false;
        }
    }

    private void SweepIfDue()
    {
        var now = _timeProvider.GetUtcNow();
        var due = Interlocked.Read(ref _sweepDue);
        if (now.ToUnixTimeMilliseconds() < due)
            return;

        var next = now.Add(SweepInterval).ToUnixTimeMilliseconds();
        if (Interlocked.CompareExchange(ref _sweepDue, next, due) != due)
            return;

        // Rows hold whole seconds, rounded down from the instant the entry must outlive, so a row
        // stamped with this very second may still guard a token for a fraction of it: only rows
        // from earlier seconds are certainly past.
        var cutoff = now.ToUnixTimeSeconds();

        // Not awaited, and not tied to the request's cancellation: the request that found the
        // sweep due should not pay for it, and cancelling it would only leave the rows for later.
        Volatile.Write(ref _sweep, Task.Run(() => SweepAsync(cutoff)));
    }

    private async Task SweepAsync(long cutoff)
    {
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            await context.Set<JtiReplayEntity>()
                .Where(e => e.ExpiresAt < cutoff)
                .ExecuteDeleteAsync();
        }
        catch (Exception ex)
        {
            // Housekeeping only: a token past its expiry is rejected on the expiry itself, so a
            // table that keeps growing is a storage problem, never a correctness one.
            _logger.LogWarning(ex, "Sweeping expired replay entries failed; they remain until the next sweep");
        }
    }
}
