using ATProtoNet.Server.Spaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ATProtoNet.Server.EntityFrameworkCore;

/// <summary>
/// EF Core-backed <see cref="ISpaceReplayStore"/>: single-use token identifiers in a relational
/// table shared by every instance.
/// </summary>
/// <remarks>
/// <para>Consuming a token is one insert. The table's primary key is <c>(iss, jti, exp)</c> —
/// exactly what the store is keyed on — so the database's uniqueness enforcement <em>is</em> the
/// replay check, with no read-modify-write to race. Two instances presented the same delegation
/// token concurrently therefore see exactly one success between them, which is the guarantee
/// <see cref="InMemorySpaceReplayStore"/> cannot give across a load balancer.</para>
/// <para>Expired rows are swept opportunistically, at most once a minute and never on the
/// caller's critical path for correctness: a token past its expiry is rejected on the expiry
/// itself, so a row that outlives its sweep costs space and nothing else. A sweep that fails —
/// a provider that cannot translate the bulk delete, a transient outage — is logged and
/// ignored rather than failing the token check it rode along with.</para>
/// <para>A relational database is a heavier hop than Redis for something on every authenticated
/// request; <see cref="ATProtoNet.Server.Redis.RedisSpaceReplayStore"/> is the lighter option
/// where one is available. Register this one with
/// <see cref="SpaceStoreExtensions.AddAtProtoEfCoreSpaceReplayStore{TContext}"/>.</para>
/// </remarks>
/// <typeparam name="TContext">
/// A <see cref="DbContext"/> carrying <see cref="SpaceReplayEntity"/>. Use
/// <see cref="SpaceDbContext"/>, or your own context configured with
/// <see cref="SpaceDbContext.ConfigureSpaceReplayModel"/>.
/// </typeparam>
public sealed class EfCoreSpaceReplayStore<TContext> : ISpaceReplayStore
    where TContext : DbContext
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly IDbContextFactory<TContext> _contextFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private long _sweepDue;

    /// <summary>
    /// Creates the store.
    /// </summary>
    /// <param name="contextFactory">Supplies a context per operation.</param>
    /// <param name="logger">Receives sweep diagnostics.</param>
    public EfCoreSpaceReplayStore(
        IDbContextFactory<TContext> contextFactory,
        ILogger<EfCoreSpaceReplayStore<TContext>>? logger = null)
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
    public EfCoreSpaceReplayStore(
        IDbContextFactory<TContext> contextFactory,
        TimeProvider timeProvider,
        ILogger<EfCoreSpaceReplayStore<TContext>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(contextFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
        _logger = logger ?? (ILogger)NullLogger.Instance;
        _sweepDue = _timeProvider.GetUtcNow().Add(SweepInterval).ToUnixTimeMilliseconds();
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryConsumeAsync(
        string issuer, string tokenId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);

        var expiry = expiresAt.ToUnixTimeSeconds();

        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        context.Add(new SpaceReplayEntity { Issuer = issuer, TokenId = tokenId, ExpiresAt = expiry });

        try
        {
            await context.SaveChangesAsync(cancellationToken);
            await SweepIfDueAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            // The insert is the check, but a key violation is not the only thing that can fail a
            // save. Confirm the identifier really is spent before calling it a replay, so a
            // genuine storage fault surfaces as an error rather than as a valid token being
            // silently refused.
            context.ChangeTracker.Clear();

            var spent = await context.Set<SpaceReplayEntity>()
                .AsNoTracking()
                .AnyAsync(
                    e => e.Issuer == issuer && e.TokenId == tokenId && e.ExpiresAt == expiry,
                    cancellationToken);

            if (!spent)
                throw;

            return false;
        }
    }

    private async Task SweepIfDueAsync(CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var due = Interlocked.Read(ref _sweepDue);
        if (now.ToUnixTimeMilliseconds() < due)
            return;

        var next = now.Add(SweepInterval).ToUnixTimeMilliseconds();
        if (Interlocked.CompareExchange(ref _sweepDue, next, due) != due)
            return;

        var cutoff = now.ToUnixTimeSeconds();

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await context.Set<SpaceReplayEntity>()
                .Where(e => e.ExpiresAt <= cutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Housekeeping only: a token past its expiry is rejected on the expiry itself, so a
            // table that keeps growing is a storage problem, never a correctness one.
            _logger.LogWarning(ex, "Sweeping expired space replay entries failed; they remain until the next sweep");
        }
    }
}
