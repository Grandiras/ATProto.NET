using ATProtoNet.Http;
using ATProtoNet.Identity;

namespace ATProtoNet.Lexicon.App.Bsky.Actor;

/// <summary>
/// Client for app.bsky.actor.* XRPC endpoints.
/// Handles profile lookups, suggestions, search, and preferences.
/// </summary>
public sealed class ActorClient
{
    private readonly XrpcClient _xrpc;

    internal ActorClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>Get a detailed profile view for an actor.</summary>
    /// <param name="actor">Handle or DID of the actor.</param>
    public Task<ProfileViewDetailed> GetProfileAsync(
        AtIdentifier actor, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams().Add("actor", actor);
        return _xrpc.QueryAsync<ProfileViewDetailed>(
            "app.bsky.actor.getProfile", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>Get detailed profiles for multiple actors (max 25 per request).</summary>
    /// <param name="actors">Handles or DIDs of the actors.</param>
    public Task<GetProfilesResponse> GetProfilesAsync(
        IEnumerable<AtIdentifier> actors, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .AddAll("actors", actors.Select(actor => actor.Value));

        return _xrpc.QueryAsync<GetProfilesResponse>(
            "app.bsky.actor.getProfiles", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>Get the authenticated user's preferences.</summary>
    public Task<GetPreferencesResponse> GetPreferencesAsync(
        CancellationToken cancellationToken = default)
    {
        return _xrpc.QueryAsync<GetPreferencesResponse>(
            "app.bsky.actor.getPreferences", cancellationToken: cancellationToken);
    }

    /// <summary>Set the authenticated user's preferences.</summary>
    /// <param name="preferences">
    /// The complete set of preferences; it replaces the stored one. Pass back the
    /// <see cref="UnknownPreference"/>s <see cref="GetPreferencesAsync"/> returned, so preferences
    /// this SDK does not model survive.
    /// </param>
    public async Task PutPreferencesAsync(
        IEnumerable<Preference> preferences, CancellationToken cancellationToken = default)
    {
        var request = new PutPreferencesRequest(Preferences: [.. preferences]);
        await _xrpc.ProcedureAsync(
            "app.bsky.actor.putPreferences", request, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Get one page of suggested accounts to follow.</summary>
    /// <param name="limit">Max results per page (1-100, default 50).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<GetSuggestionsResponse> GetSuggestionsAsync(
        int? limit = null, string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<GetSuggestionsResponse>(
            "app.bsky.actor.getSuggestions", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>Search for actors matching a query string, one page at a time.</summary>
    /// <param name="q">Search query.</param>
    /// <param name="limit">Max results per page (1-100, default 25).</param>
    /// <param name="cursor">Pagination cursor.</param>
    public Task<SearchActorsResponse> SearchActorsAsync(
        string q,
        int? limit = null,
        string? cursor = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("q", q)
            .Add("limit", limit)
            .Add("cursor", cursor);

        return _xrpc.QueryAsync<SearchActorsResponse>(
            "app.bsky.actor.searchActors", parameters, cancellationToken: cancellationToken);
    }

    /// <summary>Search for actors with typeahead (autocomplete).</summary>
    /// <param name="q">Search query prefix.</param>
    /// <param name="limit">Max results (1-100, default 10).</param>
    public Task<SearchActorsTypeaheadResponse> SearchActorsTypeaheadAsync(
        string q,
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("q", q)
            .Add("limit", limit);

        return _xrpc.QueryAsync<SearchActorsTypeaheadResponse>(
            "app.bsky.actor.searchActorsTypeahead", parameters, cancellationToken: cancellationToken);
    }
}
