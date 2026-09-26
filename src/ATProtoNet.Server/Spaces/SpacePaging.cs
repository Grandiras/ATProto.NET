namespace ATProtoNet.Server.Spaces;

/// <summary>
/// The keyset paging every space store serves its lists with: rows ordered by DID, a cursor that
/// names the last DID of the previous page, and one row fetched past the page to learn whether
/// there is another.
/// </summary>
/// <remarks>
/// A cursor that names a position rather than an offset stays valid while rows are added or
/// removed around it, which a writer set and a member list both are between two page requests.
/// </remarks>
internal static class SpacePaging
{
    /// <summary>
    /// Selects the rows after <paramref name="cursor"/>, in ordinal key order, with one extra to
    /// tell whether another page follows. For in-memory rows; a database query does the same in SQL.
    /// </summary>
    public static List<TRow> After<TRow>(IEnumerable<TRow> rows, Func<TRow, string> key, string? cursor, int limit) =>
        rows.Where(row => cursor is null || string.CompareOrdinal(key(row), cursor) > 0)
            .OrderBy(key, StringComparer.Ordinal)
            .Take(limit + 1)
            .ToList();

    /// <summary>
    /// Turns the rows <see cref="After{TRow}"/> (or its SQL equivalent) selected into a page and
    /// the cursor for the next one.
    /// </summary>
    /// <param name="rows">Up to <paramref name="limit"/> + 1 rows, ordered by key.</param>
    /// <param name="limit">The page size.</param>
    /// <param name="map">Converts a row into the item the page carries.</param>
    /// <param name="key">The item's key: the value the next page's cursor names.</param>
    /// <returns>The page, and a cursor that is <see langword="null"/> when no page follows.</returns>
    public static (List<TItem> Items, string? Cursor) Page<TRow, TItem>(
        IReadOnlyList<TRow> rows, int limit, Func<TRow, TItem> map, Func<TItem, string> key)
    {
        var items = new List<TItem>(Math.Min(rows.Count, limit));
        for (var i = 0; i < rows.Count && i < limit; i++)
            items.Add(map(rows[i]));

        return (items, rows.Count > limit && items.Count > 0 ? key(items[^1]) : null);
    }
}
