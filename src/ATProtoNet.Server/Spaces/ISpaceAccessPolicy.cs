using ATProtoNet.Lexicon.Com.AtProto.Space;
using ATProtoNet.Spaces;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// A request for a space credential, as it reaches the authority's policy.
/// </summary>
/// <param name="Space">The space being asked for.</param>
/// <param name="UserDid">The user the requesting application is acting for.</param>
/// <param name="AttestedClientId">
/// The client ID of the requesting application, when it presented a verified client attestation.
/// <see langword="null"/> means the app did not attest, not that it is unknown — an app access
/// policy that gates on identity must refuse rather than trust anything else the request says
/// about itself.
/// </param>
public sealed record SpaceAccessRequest(SpaceUri Space, string UserDid, string? AttestedClientId);

/// <summary>Why an authority answered a credential request the way it did.</summary>
public enum SpaceAccessOutcome
{
    /// <summary>Both perimeters passed; mint a credential.</summary>
    Granted,

    /// <summary>The space does not exist, or the caller may not learn that it does.</summary>
    SpaceNotFound,

    /// <summary>The space was deleted; a syncer holding a copy should drop it.</summary>
    SpaceDeleted,

    /// <summary>Refused on the basis of the requesting user.</summary>
    UserNotAuthorized,

    /// <summary>
    /// Refused on the basis of the requesting app. A client that has an attestation available
    /// retries with one on seeing this, so it is also how a space says "attest and ask again".
    /// </summary>
    AppNotAuthorized,

    /// <summary>Refused without attributing the refusal to either perimeter.</summary>
    NotAuthorized,
}

/// <summary>
/// The authority's answer to a credential request.
/// </summary>
/// <param name="Outcome">Whether to mint, and why not when not.</param>
/// <param name="Reason">An operator-facing description. Never returned to the caller.</param>
public sealed record SpaceAccessDecision(SpaceAccessOutcome Outcome, string? Reason = null)
{
    /// <summary>A decision to mint.</summary>
    public static SpaceAccessDecision Granted { get; } = new(SpaceAccessOutcome.Granted);

    /// <summary>Whether the request was granted.</summary>
    public bool IsGranted => Outcome == SpaceAccessOutcome.Granted;

    /// <summary>
    /// Refuses a request.
    /// </summary>
    /// <param name="outcome">Which perimeter refused.</param>
    /// <param name="reason">An operator-facing description.</param>
    public static SpaceAccessDecision Refuse(SpaceAccessOutcome outcome, string? reason = null) =>
        new(outcome, reason);

    /// <summary>The XRPC error name this decision is reported to the caller as.</summary>
    public string ErrorName => Outcome switch
    {
        SpaceAccessOutcome.SpaceNotFound => SpaceErrors.SpaceNotFound,
        SpaceAccessOutcome.SpaceDeleted => SpaceErrors.SpaceDeleted,
        SpaceAccessOutcome.UserNotAuthorized => SpaceErrors.UserNotAuthorized,
        SpaceAccessOutcome.AppNotAuthorized => SpaceErrors.AppNotAuthorized,
        _ => SpaceErrors.NotAuthorized,
    };
}

/// <summary>
/// Decides whether a space authority mints a credential for a given user and app.
/// </summary>
/// <remarks>
/// <para>The permissioned data protocol deliberately does not specify this. Who may read a space
/// belongs to a <em>space-management implementation</em> sitting above the protocol, identified
/// by its own Lexicon namespace, and <c>com.atproto.simplespace</c> is only the baseline every
/// PDS must offer. A service running a bespoke space type implements this instead — see
/// <see cref="SimpleSpaceAccessPolicy"/> for what the baseline looks like.</para>
/// <para>Two independent perimeters are evaluated here: the user must be authorized <b>and</b>
/// their app must be. An implementation that gates on app identity refuses with
/// <see cref="SpaceAccessOutcome.AppNotAuthorized"/> when no attestation was presented, which is
/// what prompts a client holding one to retry with it — whether a space requires an attestation
/// is not advertised anywhere else.</para>
/// </remarks>
public interface ISpaceAccessPolicy
{
    /// <summary>
    /// Evaluates a credential request.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SpaceAccessDecision> EvaluateAsync(
        SpaceAccessRequest request, CancellationToken cancellationToken = default);
}
