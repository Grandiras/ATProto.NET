using Microsoft.AspNetCore.Authorization;

namespace ATProtoNet.Server.Spaces;

// Marks a space server endpoint that authenticates its callers itself — with a space credential and its
// signed request, a delegation token, or service auth the endpoint checks — so the host's authorization does
// not apply to it.
//
// It is an IAllowAnonymous. Without it a group convention such as
// MapXrpcEndpoints().RequireAuthorization() or .RequireServiceAuth() would demand credentials these
// callers never carry — a repo host presenting a key-bound credential has no cookie — and a scheme
// authenticating the request on the group's behalf could spend a token the endpoint itself still has to
// check. The endpoint's own checks are what gate it.
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
internal sealed class AuthenticatesItselfAttribute : Attribute, IAllowAnonymous;
