namespace ATProtoNet.Identity;

/// <summary>
/// Resolves <c>did:plc</c> and <c>did:web</c> DIDs, dispatching on the method.
/// </summary>
/// <remarks>
/// <para>Nothing is cached: every call fetches. Wrap it in a <see cref="CachingDidResolver"/>
/// wherever documents are resolved repeatedly, as signature verification does.</para>
/// <para>Both methods fetch under the SDK's identity fetch policy unless constructed from
/// clients configured otherwise; see <see cref="IdentityResolverOptions"/>.</para>
/// </remarks>
public sealed class DidResolver : IDidResolver, IDisposable
{
    private readonly PlcClient _plcClient;
    private readonly DidWebResolver _webResolver;
    private readonly bool _ownsResolvers;

    /// <summary>
    /// Creates a resolver with its own <see cref="PlcClient"/> and <see cref="DidWebResolver"/>.
    /// </summary>
    /// <param name="options">Resolver options. Defaults apply when omitted.</param>
    public DidResolver(IdentityResolverOptions? options = null)
    {
        _plcClient = new PlcClient(options);
        _webResolver = new DidWebResolver(options);
        _ownsResolvers = true;
    }

    /// <summary>
    /// Creates a resolver over existing method resolvers, which the caller owns.
    /// </summary>
    /// <param name="plcClient">Resolves <c>did:plc</c>.</param>
    /// <param name="webResolver">Resolves <c>did:web</c>.</param>
    public DidResolver(PlcClient plcClient, DidWebResolver webResolver)
    {
        _plcClient = plcClient ?? throw new ArgumentNullException(nameof(plcClient));
        _webResolver = webResolver ?? throw new ArgumentNullException(nameof(webResolver));
        _ownsResolvers = false;
    }

    /// <inheritdoc/>
    /// <exception cref="DidResolutionException">
    /// Thrown with <see cref="DidResolutionErrorKind.UnsupportedMethod"/> for a method other than
    /// <c>plc</c> or <c>web</c>, or when resolution fails.
    /// </exception>
    public Task<DidDocument> ResolveAsync(Did did, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(did);

        return did.Method switch
        {
            "plc" => _plcClient.ResolveAsync(did, cancellationToken),
            "web" => _webResolver.ResolveAsync(did, cancellationToken),
            _ => Task.FromException<DidDocument>(new DidResolutionException(
                $"'{did}' uses the unsupported DID method '{did.Method}'; AT Protocol supports did:plc and did:web.",
                DidResolutionErrorKind.UnsupportedMethod, did)),
        };
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_ownsResolvers)
            return;

        _plcClient.Dispose();
        _webResolver.Dispose();
    }
}
