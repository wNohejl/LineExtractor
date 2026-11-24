namespace XLines.Web.Services;

/// <summary>
/// One call in its own scope: the shape every desk seam's single-service read had written out by
/// hand. A window outlives a request, so it never holds a DbContext across renders; each read gets a
/// fresh scope, and the scope is disposed only after the read has finished.
/// </summary>
public static class ScopeReads
{
    /// <summary>Resolves <typeparamref name="TService"/> in a new async scope and awaits <paramref name="read"/> inside it.</summary>
    public static async Task<T> InScopeAsync<TService, T>(this IServiceScopeFactory scopes, Func<TService, Task<T>> read)
        where TService : notnull
    {
        // Awaited here, not returned: handing the task back would dispose the scope, and the
        // DbContext in it, while the query was still running.
        await using var scope = scopes.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<TService>());
    }

    /// <summary>The same for a write whose result nobody reads.</summary>
    public static async Task InScopeAsync<TService>(this IServiceScopeFactory scopes, Func<TService, Task> read)
        where TService : notnull
    {
        await using var scope = scopes.CreateAsyncScope();
        await read(scope.ServiceProvider.GetRequiredService<TService>());
    }
}
