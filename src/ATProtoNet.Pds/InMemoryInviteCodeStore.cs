using System.Globalization;

namespace ATProtoNet.Pds;

/// <summary>
/// In-memory invite code store for development and testing.
/// Codes are lost when the process restarts — implement <see cref="IInviteCodeStore"/>
/// against a database for production use.
/// </summary>
public sealed class InMemoryInviteCodeStore : IInviteCodeStore
{
    // A single gate rather than a ConcurrentDictionary: claiming has to read the remaining
    // use count and write it back as one atomic step, which a concurrent dictionary alone
    // cannot express. Invite traffic is low, so one lock is not a bottleneck.
    private readonly Lock _gate = new();
    private readonly Dictionary<string, PdsInviteCode> _codes = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public Task CreateAsync(PdsInviteCode code, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(code);

        lock (_gate)
        {
            if (!_codes.TryAdd(code.Code, code))
                throw new InvalidOperationException($"Invite code '{code.Code}' already exists.");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PdsInviteCode?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_codes.TryGetValue(code, out var entry) ? Snapshot(entry) : null);
        }
    }

    /// <inheritdoc />
    public Task<bool> TryClaimAsync(string code, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(code)) return Task.FromResult(false);

        lock (_gate)
        {
            if (!_codes.TryGetValue(code, out var entry)) return Task.FromResult(false);
            if (entry.Disabled || entry.ClaimedUses >= entry.AvailableUses) return Task.FromResult(false);

            entry.ClaimedUses++;
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task ConfirmClaimAsync(string code, string usedByDid, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_codes.TryGetValue(code, out var entry))
                entry.Uses.Add(new PdsInviteCodeUse { UsedBy = usedByDid });
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ReleaseClaimAsync(string code, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_codes.TryGetValue(code, out var entry) && entry.ClaimedUses > 0)
                entry.ClaimedUses--;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<InviteCodePage> ListAsync(InviteCodeQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var limit = query.Limit <= 0 ? 100 : query.Limit;
        var offset = ParseCursor(query.Cursor);

        lock (_gate)
        {
            IEnumerable<PdsInviteCode> codes = _codes.Values;

            if (query.ForAccount is not null)
                codes = codes.Where(c => string.Equals(c.ForAccount, query.ForAccount, StringComparison.Ordinal));

            // Ties are broken on the code itself so paging stays stable across calls.
            codes = query.Sort == InviteCodeSort.Usage
                ? codes.OrderByDescending(c => c.Uses.Count).ThenBy(c => c.Code, StringComparer.Ordinal)
                : codes.OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Code, StringComparer.Ordinal);

            var ordered = codes.ToList();
            var page = ordered.Skip(offset).Take(limit).Select(Snapshot).ToList();
            var next = offset + page.Count;

            return Task.FromResult(new InviteCodePage
            {
                Codes = page,
                Cursor = next < ordered.Count ? next.ToString(CultureInfo.InvariantCulture) : null,
            });
        }
    }

    /// <inheritdoc />
    public Task<int> DisableAsync(IEnumerable<string> codes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(codes);

        var disabled = 0;
        lock (_gate)
        {
            foreach (var code in codes)
            {
                if (_codes.TryGetValue(code, out var entry) && !entry.Disabled)
                {
                    entry.Disabled = true;
                    disabled++;
                }
            }
        }

        return Task.FromResult(disabled);
    }

    /// <inheritdoc />
    public Task<int> DisableForAccountsAsync(IEnumerable<string> accountDids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(accountDids);

        var accounts = new HashSet<string>(accountDids, StringComparer.Ordinal);
        var disabled = 0;

        lock (_gate)
        {
            foreach (var entry in _codes.Values)
            {
                if (entry.Disabled || entry.ForAccount is null) continue;
                if (!accounts.Contains(entry.ForAccount)) continue;

                entry.Disabled = true;
                disabled++;
            }
        }

        return Task.FromResult(disabled);
    }

    private static int ParseCursor(string? cursor)
    {
        if (string.IsNullOrEmpty(cursor)) return 0;
        return int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out var offset) && offset >= 0
            ? offset
            : 0;
    }

    // Hand out copies so a caller mutating the result can't corrupt the claim counters.
    private static PdsInviteCode Snapshot(PdsInviteCode entry) => new()
    {
        Code = entry.Code,
        AvailableUses = entry.AvailableUses,
        ClaimedUses = entry.ClaimedUses,
        Disabled = entry.Disabled,
        ForAccount = entry.ForAccount,
        CreatedBy = entry.CreatedBy,
        CreatedAt = entry.CreatedAt,
        Uses = [.. entry.Uses],
    };
}
