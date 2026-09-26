using Microsoft.AspNetCore.Authorization;

namespace ATProtoNet.Server.Spaces;

/// <summary>
/// Marks a space server endpoint that authenticates its callers itself — with a space credential
/// and its DPoP proof, a delegation token, or service auth the endpoint checks — so the host's
/// authorization does not apply to it.
/// </summary>
/// <remarks>
/// It is an <see cref="IAllowAnonymous"/>. Without it a group convention such as
/// <c>MapXrpcEndpoints().RequireAuthorization()</c> or <c>.RequireServiceAuth()</c> would demand
/// credentials these callers never carry — a repo host presenting a DPoP-bound credential has no
/// cookie — and a scheme authenticating the request on the group's behalf could spend a token the
/// endpoint itself still has to check. The endpoint's own checks are what gate it.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class AuthenticatesItselfAttribute : Attribute, IAllowAnonymous;
