using ATProtoNet.Identity;

namespace ATProtoNet.Server.Spaces;

// The keyset paging every space store serves its lists with: rows ordered by DID, a cursor that names
// the last DID of the previous page, and one row fetched past the page to learn whether there is
// another.
//
// A cursor that names a position rather than an offset stays valid while rows are added or removed
// around it, which a writer set and a member list both are between two page requests.
internal static class SpacePaging
{
    // Selects the rows after cursor, in ordinal key order, with one extra to tell whether another page
    // follows. For in-memory rows; a database query does the same in SQL.
    public static List<TRow> After<TRow>(IEnumerable<TRow> rows, Func<TRow, string> key, string? cursor, int limit) =>
        rows.Where(row => cursor is null || string.CompareOrdinal(key(row), cursor) > 0)
            .OrderBy(key, StringComparer.Ordinal)
            .Take(limit + 1)
            .ToList();

    // Turns the rows After (or its SQL equivalent) selected into a page and the cursor for the next one.
    //
    // rows: Up to limit + 1 rows, ordered by key.
    //
    // limit: The page size.
    //
    // map: Converts a row into the item the page carries.
    //
    // key: The item's key: the value the next page's cursor names.
    //
    // Returns: The page, and a cursor that is null when no page follows.
    public static (List<TItem> Items, string? Cursor) Page<TRow, TItem>(
        IReadOnlyList<TRow> rows, int limit, Func<TRow, TItem> map, Func<TItem, string> key)
    {
        var items = new List<TItem>(Math.Min(rows.Count, limit));
        for (var i = 0; i < rows.Count && i < limit; i++)
            items.Add(map(rows[i]));

        return (items, rows.Count > limit && items.Count > 0 ? key(items[^1]) : null);
    }

    // The Page of a list ordered by space revision, whose cursor is a checkpoint rather than a continuation:
    // every non-empty page carries its last key (a syncer persists it as the position it has processed to,
    // whether or not more follows) and an empty page carries none.
    public static (List<TItem> Items, string? Cursor) CheckpointPage<TRow, TItem>(
        IReadOnlyList<TRow> rows, int limit, Func<TRow, TItem> map, Func<TItem, string> key)
    {
        var (items, _) = Page(rows, limit, map, key);
        return (items, items.Count > 0 ? key(items[^1]) : null);
    }

    // The revision for a space's next update: a fresh TID, or one past the previous revision when the clock
    // has not moved beyond it (or has stepped back), so revisions only ever increase. One timestamp tick
    // past the previous one keeps the fresh TID's clock identifier, as the reference implementation does.
    public static Tid NextSpaceRev(Tid? previous)
    {
        var fresh = Tid.Next();
        if (previous is null || fresh.CompareTo(previous) > 0)
            return fresh;

        const int ClockIdBits = 10;
        return Tid.FromInt64((((previous.ToInt64() >> ClockIdBits) + 1) << ClockIdBits) | (fresh.ToInt64() & ((1 << ClockIdBits) - 1)));
    }
}
