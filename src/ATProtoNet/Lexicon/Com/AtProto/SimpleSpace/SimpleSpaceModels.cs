using System.Text.Json.Serialization;

namespace ATProtoNet.Lexicon.Com.AtProto.SimpleSpace;

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.defs
// ──────────────────────────────────────────────────────────────

/// <summary>
/// How a <c>simplespace</c> authority decides whether to authorize a <em>user</em>: as a
/// space's read policy, whether to mint them a credential; as its write policy, whether to track
/// their writes and forward their write notifications.
/// </summary>
/// <remarks>
/// <para>For a read, the user must be authorized by the read policy <b>and</b> their app by the
/// <see cref="SimpleSpaceAppAccess">app access policy</see> for a credential to be minted. A
/// write is judged by the write policy alone: its notification comes from the writer's repo
/// host rather than from an app, so there is no app to judge.</para>
/// <para>The union is open at the schema layer, and a host rejects a variant it does not
/// implement at create/update time rather than storing a policy it could not enforce.</para>
/// </remarks>
[JsonDerivedType(typeof(PublicPolicy), SimpleSpaceTypes.PublicPolicy)]
[JsonDerivedType(typeof(MemberListPolicy), SimpleSpaceTypes.MemberListPolicy)]
[JsonDerivedType(typeof(ManagingAppPolicy), SimpleSpaceTypes.ManagingAppPolicy)]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
public abstract class SimpleSpaceUserPolicy;

/// <summary>Any user is authorized.</summary>
public sealed class PublicPolicy : SimpleSpaceUserPolicy;

/// <summary>
/// Only users on the space's member list are authorized. This is the default for both the read
/// and the write policy.
/// </summary>
/// <remarks>
/// <para>Each member carries separate read and write access (see <see cref="SimpleSpaceMember"/>):
/// under a member-list read policy a member is admitted to read when their <c>read</c> flag is
/// set, and under a member-list write policy their writes are tracked when their <c>write</c>
/// flag is set.</para>
/// <para>The member list is host-internal state. It is not a synced protocol structure and is
/// never enumerated to the network — <c>listRepos</c> returns the writers the write policy
/// admitted, not the member list.</para>
/// </remarks>
public sealed class MemberListPolicy : SimpleSpaceUserPolicy;

/// <summary>
/// The managing app is asked, per request, whether to authorize each user.
/// </summary>
/// <remarks>
/// The authority calls <c>com.atproto.simplespace.checkUserAccess</c> on the managing app,
/// passing the space, the user, and the kind of access being checked — <c>read</c> at
/// credential-mint time, together with the attested client ID, and <c>write</c> when a write
/// notification arrives, without one. This is what enables dynamic policies — follower-gating,
/// paid subscriptions, join approvals — without an app maintaining an explicit list.
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
/// <remarks>
/// It applies to reads only. A write notification comes from the writer's repo host, which
/// presents no client attestation, so it is judged by the write policy alone.
/// </remarks>
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

/// <summary>
/// The kinds of access <c>com.atproto.simplespace.checkUserAccess</c> asks a managing app about
/// (the known values of its <c>access</c> parameter).
/// </summary>
public static class SimpleSpaceAccess
{
    /// <summary>Whether the user may read the space. Asked when minting a credential.</summary>
    public const string Read = "read";

    /// <summary>
    /// Whether the authority should track the user's writes and forward their write
    /// notifications. Asked when a write notification arrives.
    /// </summary>
    public const string Write = "write";
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

    /// <summary>How the authority decides whether to authorize a user to read the space.</summary>
    [JsonPropertyName("readPolicy")]
    public required SimpleSpaceUserPolicy ReadPolicy { get; init; }

    /// <summary>
    /// How the authority decides whether to track a user's writes and forward their write
    /// notifications.
    /// </summary>
    [JsonPropertyName("writePolicy")]
    public required SimpleSpaceUserPolicy WritePolicy { get; init; }

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

    /// <summary>Replaces the current read policy wholesale when supplied.</summary>
    [JsonPropertyName("readPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SimpleSpaceUserPolicy? ReadPolicy { get; init; }

    /// <summary>Replaces the current write policy wholesale when supplied.</summary>
    [JsonPropertyName("writePolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SimpleSpaceUserPolicy? WritePolicy { get; init; }

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

    /// <summary>How the authority decides whether to authorize a user to read the space.</summary>
    [JsonPropertyName("readPolicy")]
    public required SimpleSpaceUserPolicy ReadPolicy { get; init; }

    /// <summary>
    /// How the authority decides whether to track a user's writes and forward their write
    /// notifications.
    /// </summary>
    [JsonPropertyName("writePolicy")]
    public required SimpleSpaceUserPolicy WritePolicy { get; init; }

    /// <summary>How the authority decides whether to authorize a requesting app.</summary>
    [JsonPropertyName("appAccess")]
    public required SimpleSpaceAppAccess AppAccess { get; init; }
}

// ──────────────────────────────────────────────────────────────
//  com.atproto.simplespace.putMember / removeMember / listMembers
// ──────────────────────────────────────────────────────────────

/// <summary>
/// Request body for <c>putMember</c>: adds a member, or replaces an existing member's read and
/// write access.
/// </summary>
public sealed class PutSimpleSpaceMemberRequest
{
    /// <summary>Reference to the space.</summary>
    [JsonPropertyName("space")]
    public required string Space { get; init; }

    /// <summary>The DID of the member.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>Whether the member may read under a member-list read policy.</summary>
    [JsonPropertyName("read")]
    public required bool Read { get; init; }

    /// <summary>Whether the member's writes are tracked under a member-list write policy.</summary>
    [JsonPropertyName("write")]
    public required bool Write { get; init; }
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

/// <summary>A member of a <c>simplespace</c> space, and their access.</summary>
/// <remarks>
/// The two flags are independent: a member may be read-only, write-only, both, or on the list
/// but admitted to neither. Each applies only where the corresponding policy is a
/// <see cref="MemberListPolicy"/>.
/// </remarks>
public sealed class SimpleSpaceMember
{
    /// <summary>The member's DID.</summary>
    [JsonPropertyName("did")]
    public required string Did { get; init; }

    /// <summary>Whether the member may read under a member-list read policy.</summary>
    [JsonPropertyName("read")]
    public required bool Read { get; init; }

    /// <summary>Whether the member's writes are tracked under a member-list write policy.</summary>
    [JsonPropertyName("write")]
    public required bool Write { get; init; }
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

    /// <summary>A requested read or write policy is not one the host implements.</summary>
    public const string UnsupportedPolicy = "UnsupportedPolicy";

    /// <summary>The requested app access variant is not one the host implements.</summary>
    public const string UnsupportedAppAccess = "UnsupportedAppAccess";
}
