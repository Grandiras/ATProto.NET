using System.Runtime.CompilerServices;
using ATProtoNet.Models;

namespace ATProtoNet.Http;

/// <summary>
/// The one cursor-pagination loop behind every <c>Enumerate*</c> method.
/// </summary>
internal static class Pagination
{
    /// <summary>
    /// Fetches pages until the server stops returning a new cursor, yielding each page's items
    /// as it arrives.
    /// </summary>
    /// <typeparam name="TPage">The page type.</typeparam>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="fetchPage">
    /// Fetches the page after a cursor; <see langword="null"/> asks for the first page.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <remarks>
    /// The walk ends after a page whose cursor is <see langword="null"/>, empty, or one the
    /// server already returned. A server that repeats a cursor would otherwise be asked for the
    /// same page forever; every cursor is remembered, so a cycle through several ends too.
    /// </remarks>
    public static async IAsyncEnumerable<T> EnumerateAsync<TPage, T>(
        Func<string?, CancellationToken, Task<TPage>> fetchPage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
        where TPage : ICursorPage<T>
    {
        HashSet<string>? seen = null;
        string? cursor = null;
        while (true)
        {
            var page = await fetchPage(cursor, cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
                yield return item;

            cursor = page.Cursor;
            if (string.IsNullOrEmpty(cursor) || !(seen ??= new HashSet<string>(StringComparer.Ordinal)).Add(cursor))
                yield break;
        }
    }
}
