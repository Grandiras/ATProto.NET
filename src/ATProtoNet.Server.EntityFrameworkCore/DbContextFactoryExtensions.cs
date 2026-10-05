using Microsoft.EntityFrameworkCore;

namespace ATProtoNet.Server.EntityFrameworkCore;

internal static class DbContextFactoryExtensions
{
    // Runs work on a context of its own, disposed when it finishes. A statement that returns nothing
    // (ExecuteUpdateAsync, ExecuteDeleteAsync) is awaited and its row count dropped.
    public static async Task<TResult> UseAsync<TContext, TResult>(
        this IDbContextFactory<TContext> factory, Func<TContext, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var scope = context.ConfigureAwait(false);
        return await work(context, cancellationToken).ConfigureAwait(false);
    }
}
