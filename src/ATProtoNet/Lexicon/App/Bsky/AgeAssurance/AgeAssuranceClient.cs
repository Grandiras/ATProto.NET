using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.App.Bsky.AgeAssurance;

/// <summary>
/// Client for app.bsky.ageassurance.* XRPC endpoints: the age checks some jurisdictions require
/// before an account may see all content.
/// </summary>
public sealed class AgeAssuranceClient
{
    private readonly XrpcClient _xrpc;

    internal AgeAssuranceClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Start age assurance for the authenticated account. The provider emails the instructions.
    /// </summary>
    /// <param name="email">The address to send the instructions to.</param>
    /// <param name="language">The language to communicate in, such as <c>en</c>.</param>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 code of the user's country.</param>
    /// <param name="regionCode">The ISO 3166-2 code of the user's region, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The account's state after starting.</returns>
    /// <exception cref="XrpcException">
    /// One of <see cref="AgeAssuranceErrors"/>, such as <see cref="AgeAssuranceErrors.RegionNotSupported"/>.
    /// </exception>
    public Task<AgeAssuranceState> BeginAsync(
        string email,
        string language,
        string countryCode,
        string? regionCode = null,
        CancellationToken cancellationToken = default)
    {
        var request = new BeginRequest
        {
            Email = email,
            Language = language,
            CountryCode = countryCode,
            RegionCode = regionCode,
        };

        return _xrpc.ProcedureAsync<AgeAssuranceState>(
            "app.bsky.ageassurance.begin", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the age assurance configuration: per region, the minimum age and the rules that
    /// decide an account's access.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<AgeAssuranceConfig> GetConfigAsync(CancellationToken cancellationToken = default)
    {
        return _xrpc.QueryAsync<AgeAssuranceConfig>(
            "app.bsky.ageassurance.getConfig", cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Get the authenticated account's age assurance state, and what a client needs to compute
    /// it itself.
    /// </summary>
    /// <param name="countryCode">The ISO 3166-1 alpha-2 code of the user's country.</param>
    /// <param name="regionCode">The ISO 3166-2 code of the user's region, if any.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GetStateResponse> GetStateAsync(
        string countryCode, string? regionCode = null, CancellationToken cancellationToken = default)
    {
        var parameters = new XrpcParams()
            .Add("countryCode", countryCode)
            .Add("regionCode", regionCode);

        return _xrpc.QueryAsync<GetStateResponse>(
            "app.bsky.ageassurance.getState", parameters, cancellationToken: cancellationToken);
    }
}
