using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.defs
// ──────────────────────────────────────────────────────────────

/// <summary>
/// How a <c>simplespace</c> authority decides whether to authorize a requesting <em>user</em>.
/// </summary>
/// <remarks>
/// A user must be authorized by the policy <b>and</b> their app by the
/// <see cref="SimpleSpaceAppAccess">app access policy</see> for a credential to be minted.
/// The union is open at the schema layer, and a host rejects a variant it does not implement at
/// create/update time rather than storing a policy it could not enforce.
/// </remarks>
[JsonDerivedType(typeof(PublicPolicy), SimpleSpaceTypes.PublicPolicy)]
[JsonDerivedType(typeof(MemberListPolicy), SimpleSpaceTypes.MemberListPolicy)]
[JsonDerivedType(typeof(ManagingAppPolicy), SimpleSpaceTypes.ManagingAppPolicy)]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
public abstract class SimpleSpaceUserPolicy;

/// <summary>Any requester is authorized.</summary>
public sealed class PublicPolicy : SimpleSpaceUserPolicy;

/// <summary>
/// Only users on the space's member list are authorized. This is the default.
/// </summary>
/// <remarks>
/// The member list is host-internal state consulted at credential-mint time. It is not a synced
/// protocol structure and is never enumerated to the network — <c>listRepos</c> returns writers,
/// not readers.
/// </remarks>
public sealed class MemberListPolicy : SimpleSpaceUserPolicy;

/// <summary>
/// The managing app is asked, per request, whether to authorize each user.
/// </summary>
/// <remarks>
/// At mint time the authority calls <c>com.atproto.simplespace.checkUserAccess</c> on the
/// managing app, passing the space, the requesting user, and the attested client ID. This is
/// what enables dynamic policies — follower-gating, paid subscriptions, join approvals —
/// without an app maintaining an explicit list.
/// </remarks>
public sealed class ManagingAppPolicy : SimpleSpaceUserPolicy
{
    /// <summary>
    /// Service identifier of the managing app: a DID with an optional service fragment
    /// (e.g. <c>did:web:example.com#forum</c>).
    /// </summary>
    [JsonPropertyName("managingApp")]
    public required string ManagingApp { get; init; }
}

/// <summary>
/// How a <c>simplespace</c> authority decides whether to authorize a requesting <em>app</em>.
/// </summary>
[JsonDerivedType(typeof(OpenAppAccess), SimpleSpaceTypes.Open)]
[JsonDerivedType(typeof(AllowListAppAccess), SimpleSpaceTypes.AllowList)]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
public abstract class SimpleSpaceAppAccess;

/// <summary>
/// Any application may access the space. This is the default, and requires no client
/// attestation — so public clients work.
/// </summary>
public sealed class OpenAppAccess : SimpleSpaceAppAccess;

/// <summary>
/// Only the named clients may access the space.
/// </summary>
/// <remarks>
/// The list is evaluated against the <em>attested</em> client ID — the <c>iss</c> of a verified
/// client attestation — so it is enforceable rather than advisory.
/// </remarks>
public sealed class AllowListAppAccess : SimpleSpaceAppAccess
{
    /// <summary>The OAuth client IDs permitted to access the space.</summary>
    [JsonPropertyName("allowed")]
    public required List<string> Allowed { get; init; }
}

/// <summary>The <c>$type</c> discriminators for the <c>com.atproto.simplespace.defs</c> unions.</summary>
public static class SimpleSpaceTypes
{
    /// <summary>Discriminator for <see cref="SimpleSpace.PublicPolicy"/>.</summary>
    public const string PublicPolicy = "com.atproto.simplespace.defs#publicPolicy";

    /// <summary>Discriminator for <see cref="SimpleSpace.MemberListPolicy"/>.</summary>
    public const string MemberListPolicy = "com.atproto.simplespace.defs#memberListPolicy";

    /// <summary>Discriminator for <see cref="SimpleSpace.ManagingAppPolicy"/>.</summary>
    public const string ManagingAppPolicy = "com.atproto.simplespace.defs#managingAppPolicy";

    /// <summary>Discriminator for <see cref="OpenAppAccess"/>.</summary>
    public const string Open = "com.atproto.simplespace.defs#open";

    /// <summary>Discriminator for <see cref="AllowListAppAccess"/>.</summary>
    public const string AllowList = "com.atproto.simplespace.defs#allowList";
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.createSpace
// ──────────────────────────────────────────────────────────────

/// <summary>Request body for <c>createSpace</c>.</summary>
public sealed class CreateSimpleSpaceRequest
{
    /// <summary>
    /// The NSID of the space type, describing the modality of the space
    /// (e.g. <c>app.bsky.group</c>).
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// The space key, distinguishing multiple spaces of the same type under the same owner.
    /// A TID is generated when omitted.
    /// </summary>
    [JsonPropertyName("skey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Skey { get; init; }

    /// <summary>How the authority decides whether to authorize a requesting user.</summary>
    [JsonPropertyName("policy")]
    public required SimpleSpaceUserPolicy Policy { get; init; }

    /// <summary>How the authority decides whether to authorize a requesting app.</summary>
    [JsonPropertyName("appAccess")]
    public required SimpleSpaceAppAccess AppAccess { get; init; }
}

/// <summary>Response from <c>createSpace</c>.</summary>
public sealed class CreateSimpleSpaceResponse
{
    /// <summary>URI of the created space.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>Parses <see cref="Uri"/> into its authority, type, and key components.</summary>
    public Spaces.SpaceUri ToSpaceUri() => Spaces.SpaceUri.Parse(Uri);
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.updateSpace / deleteSpace
// ──────────────────────────────────────────────────────────────

/// <summary>Request body for <c>updateSpace</c>. Omitted fields are left unchanged.</summary>
public sealed class UpdateSimpleSpaceRequest
{
    /// <summary>Reference to the space to update.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>Replaces the current user policy wholesale when supplied.</summary>
    [JsonPropertyName("policy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SimpleSpaceUserPolicy? Policy { get; init; }

    /// <summary>Replaces the current app access policy wholesale when supplied.</summary>
    [JsonPropertyName("appAccess")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SimpleSpaceAppAccess? AppAccess { get; init; }
}

/// <summary>Request body for <c>deleteSpace</c>.</summary>
public sealed class DeleteSimpleSpaceRequest
{
    /// <summary>Reference to the space to delete.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.getSpace
// ──────────────────────────────────────────────────────────────

/// <summary>Response from <c>getSpace</c>: a space and its configuration.</summary>
public sealed class GetSimpleSpaceResponse
{
    /// <summary>URI of the space.</summary>
    [JsonPropertyName("uri")]
    public required string Uri { get; init; }

    /// <summary>How the authority decides whether to authorize a requesting user.</summary>
    [JsonPropertyName("policy")]
    public required SimpleSpaceUserPolicy Policy { get; init; }

    /// <summary>How the authority decides whether to authorize a requesting app.</summary>
    [JsonPropertyName("appAccess")]
    public required SimpleSpaceAppAccess AppAccess { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.addMember / removeMember / listMembers
// ──────────────────────────────────────────────────────────────

/// <summary>Request body for <c>addMember</c>.</summary>
public sealed class AddSimpleSpaceMemberRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the member to add.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>Request body for <c>removeMember</c>.</summary>
public sealed class RemoveSimpleSpaceMemberRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the member to remove.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>A member of a <c>simplespace</c> space.</summary>
public sealed class SimpleSpaceMember
{
    /// <summary>The member's DID.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }
}

/// <summary>Response from <c>listMembers</c>.</summary>
public sealed class ListSimpleSpaceMembersResponse
{
    /// <summary>
    /// Pagination cursor; pass this back on the next request to continue where this page ended.
    /// <see langword="null"/> when there are no further results.
    /// </summary>
    [JsonPropertyName("cursor")]
    public string? Cursor { get; init; }

    /// <summary>The current members.</summary>
    [JsonPropertyName("members")]
    public required List<SimpleSpaceMember> Members { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.checkUserAccess
// ──────────────────────────────────────────────────────────────

/// <summary>Response from <c>checkUserAccess</c>, served by a space's managing app.</summary>
public sealed class CheckUserAccessResponse
{
    /// <summary>Whether the managing app authorizes the request.</summary>
    [JsonPropertyName("authorized")]
    public required bool Authorized { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  Errors
// ──────────────────────────────────────────────────────────────

/// <summary>The named errors the <c>com.atproto.simplespace.*</c> endpoints return.</summary>
public static class SimpleSpaceErrors
{
    /// <summary>The space does not exist.</summary>
    public const string SpaceNotFound = "SpaceNotFound";

    /// <summary>A space with this owner, type, and key already exists.</summary>
    public const string SpaceAlreadyExists = "SpaceAlreadyExists";

    /// <summary>The authenticated user is not the space owner.</summary>
    public const string NotSpaceOwner = "NotSpaceOwner";

    /// <summary>The requested user policy is not one the host implements.</summary>
    public const string UnsupportedPolicy = "UnsupportedPolicy";

    /// <summary>The requested app access variant is not one the host implements.</summary>
    public const string UnsupportedAppAccess = "UnsupportedAppAccess";
}
