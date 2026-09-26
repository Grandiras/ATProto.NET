using ATProtoNet.Auth;
using ATProtoNet.Identity;
using Microsoft.Extensions.Logging;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Signs the space server's outbound service auth as an account this service hosts, rather than
/// as the service itself.
/// </summary>
/// <remarks>
/// <para>Some space calls are only accepted from a particular account. The reference space
/// authority accepts <c>com.atproto.space.notifyWrite</c> only when its <c>iss</c> is the writer
/// whose repo advanced (and a syncer accepts a forwarded one from the space's authority), and a
/// managing app accepts <c>com.atproto.simplespace.checkUserAccess</c> only from the space's
/// authority. A PDS signs those as the account, with the account's <c>#atproto</c> key, which is
/// what this seam lets a service built on this SDK do.</para>
/// <para>Who each call is signed as:</para>
/// <list type="bullet">
/// <item><description>a repo host's <c>notifyWrite</c> (<see cref="SpaceWriteNotifier.NotifyWriteAsync"/>):
/// the writer;</description></item>
/// <item><description>an authority's forwarded <c>notifyWrite</c> and its
/// <c>notifySpaceDeleted</c>: the space's authority;</description></item>
/// <item><description>an authority's <c>checkUserAccess</c> to a managing app: the space's
/// authority.</description></item>
/// </list>
/// <para>The signer is asked for every account, the service's own DID included, so it can also
/// supply the service's <c>#atproto</c> key when the authority signs credentials with a dedicated
/// <c>#atproto_space</c> one. When no signer is registered, or it returns <see langword="null"/>
/// for an account, the call is signed with the service's own key as
/// <see cref="SpaceServerOptions.ServiceDid"/> — which is enough whenever that DID <em>is</em> the
/// account, as for a dedicated authority.</para>
/// <para>Register an implementation as a singleton before <c>AddSpaceAuthority</c> or
/// <c>AddSimpleSpace</c>.</para>
/// </remarks>
/// <example>
/// <code>
/// public sealed class ActorStoreSigner(IActorStore actors) : ISpaceAccountSigner
/// {
///     private readonly ConcurrentDictionary&lt;Did, ServiceAuthGenerator&gt; _signers = new();
///
///     public async ValueTask&lt;ServiceAuthGenerator?&gt; GetSignerAsync(Did account, CancellationToken ct)
///     {
///         if (_signers.TryGetValue(account, out var signer))
///             return signer;
///
///         var key = await actors.GetSigningKeyAsync(account, ct);
///         return key is null ? null : _signers.GetOrAdd(account, did => new ServiceAuthGenerator(did, key));
///     }
/// }
/// </code>
/// </example>
public interface ISpaceAccountSigner
{
    /// <summary>
    /// Returns a generator that signs as <paramref name="account"/>, or <see langword="null"/>
    /// when this service holds no key for it.
    /// </summary>
    /// <param name="account">The account the call must come from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A generator whose <see cref="ServiceAuthGenerator.ServiceDid"/> is
    /// <paramref name="account"/>, signing with the key its DID document publishes at
    /// <c>#atproto</c> (or the one its <see cref="ServiceAuthGenerator.KeyId"/> names). The
    /// signer keeps ownership: callers never dispose it.
    /// </returns>
    ValueTask<ServiceAuthGenerator?> GetSignerAsync(Did account, CancellationToken cancellationToken = default);
}

/// <summary>
/// Picks the generator an outbound call is signed with: the account's own, when an
/// <see cref="ISpaceAccountSigner"/> holds one, and the service's otherwise.
/// </summary>
internal static class SpaceAccountSigning
{
    /// <summary>
    /// Returns the account's generator, or <paramref name="serviceAuth"/> when there is no
    /// account signer, it holds no key for the account, or it fails.
    /// </summary>
    /// <remarks>
    /// A signer that fails is not a reason to drop the call: the service key is what the call
    /// would have carried without one, and a receiver that insists on the account refuses it,
    /// which a best-effort notification or a refused access check already handles.
    /// </remarks>
    public static async ValueTask<ServiceAuthGenerator> ChooseAsync(
        ISpaceAccountSigner? accountSigner,
        ServiceAuthGenerator serviceAuth,
        Did account,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (accountSigner is null)
            return serviceAuth;

        ServiceAuthGenerator? signer;
        try
        {
            signer = await accountSigner.GetSignerAsync(account, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Signing as {Account} failed; signing as the service instead.", account);
            return serviceAuth;
        }

        if (signer is null)
            return serviceAuth;

        if (signer.ServiceDid != account)
        {
            logger.LogWarning(
                "The account signer returned a generator for {Signer} when asked for {Account}; signing as the service instead.",
                signer.ServiceDid, account);
            return serviceAuth;
        }

        return signer;
    }
}
