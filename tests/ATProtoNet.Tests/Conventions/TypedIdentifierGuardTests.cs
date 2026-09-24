using System.Reflection;
using System.Text.RegularExpressions;
using ATProtoNet.Server.Authentication;

namespace ATProtoNet.Tests.Conventions;

/// <summary>
/// Guards the typed-identifier convention (#119): no public model property or client method
/// parameter that carries a Lexicon identifier format — DID, handle, AT identifier, AT URI,
/// NSID, CID, record key, TID or datetime — is a <see cref="string"/>.
/// </summary>
/// <remarks>
/// <para>Members are recognized by name: the last camel-case word of the property or parameter
/// (<c>did</c>, <c>uri</c>, <c>cid</c>, <c>createdAt</c> → <c>at</c>, …) against a list mined
/// from the upstream Lexicons. A <see cref="string"/>, a string array or a string sequence under
/// such a name fails, unless it is one of the <see cref="Exceptions"/>.</para>
/// <para>The scan covers the Lexicon models and clients and the types around them. It leaves
/// out code that is not a Lexicon surface: the identifier types themselves, transport,
/// serialization, crypto and CAR/MST internals. DID documents and PLC operations
/// (<c>ATProtoNet.Identity</c>) are reworked by #120; the OAuth session model
/// (<c>ATProtoNet.Auth.OAuth</c>) and the token stores by #124.</para>
/// </remarks>
public class TypedIdentifierGuardTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(AtProtoClient).Assembly,
        typeof(AtProtoAuthenticationHandler).Assembly,
    ];

    /// <summary>The namespaces scanned: an entry ending in <c>.*</c> also covers its children.</summary>
    private static readonly string[] ScannedNamespaces =
    [
        "ATProtoNet",
        "ATProtoNet.Admin",
        "ATProtoNet.Auth",
        "ATProtoNet.Models",
        "ATProtoNet.Lexicon.*",
        "ATProtoNet.Spaces.*",
        "ATProtoNet.Streaming.*",
        "ATProtoNet.Server.Spaces.*",
    ];

    /// <summary>
    /// Areas #119 converts in its later PRs. Each PR removes its entries; the list must end
    /// empty. An entry that no longer matches any violation fails
    /// <see cref="PendingConversions_AllStillHaveViolations"/>, so it cannot linger.
    /// </summary>
    private static readonly string[] PendingConversions =
    [
        // PR 4: spaces and streaming, including the com.atproto.sync.subscribeRepos event
        // models the firehose reads.
        "ATProtoNet.Lexicon.Com.AtProto.Space.",
        "ATProtoNet.Lexicon.Com.AtProto.SimpleSpace.",
        "ATProtoNet.Spaces.",
        "ATProtoNet.Streaming.",
        "ATProtoNet.Server.Spaces.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.FirehoseMessage.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.CommitEvent.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.RepoOp.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.SyncEvent.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.IdentityEvent.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.AccountEvent.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.HandleEvent.",
        "ATProtoNet.Lexicon.Com.AtProto.Sync.TombstoneEvent.",
        // Mints the service-auth JWTs the spaces server sends; typed with its consumers.
        "ATProtoNet.Auth.ServiceAuthGenerator.",
    ];

    /// <summary>
    /// Members whose name matches but whose value is genuinely not one identifier format.
    /// Keyed <c>Namespace.Type.Property</c> or <c>Namespace.Type.Method(parameter)</c>.
    /// </summary>
    private static readonly Dictionary<string, string> Exceptions = new(StringComparer.Ordinal)
    {
        ["ATProtoNet.Models.Label.Uri"] =
            "Lexicon format `uri`: a record label's subject is an AT URI, an account label's a bare DID.",
        ["ATProtoNet.Lexicon.App.Bsky.Embed.ExternalInfo.Uri"] =
            "Lexicon format `uri`: the linked web page's URL.",
        ["ATProtoNet.Lexicon.App.Bsky.Embed.ExternalViewInfo.Uri"] =
            "Lexicon format `uri`: the linked web page's URL.",
        ["ATProtoNet.Lexicon.App.Bsky.RichText.LinkFeature.Uri"] =
            "Lexicon format `uri`: the link facet's target URL.",
        ["ATProtoNet.Lexicon.App.Bsky.RichText.RichTextBuilder.Link(uri)"] =
            "Lexicon format `uri`: the link facet's target URL.",
        ["ATProtoNet.Lexicon.App.Bsky.Feed.FeedClient.SearchPostsAsync(since)"] =
            "No Lexicon format: a datetime or a bare ISO date (YYYY-MM-DD).",
        ["ATProtoNet.Lexicon.App.Bsky.Feed.FeedClient.SearchPostsAsync(until)"] =
            "No Lexicon format: a datetime or a bare ISO date (YYYY-MM-DD).",
        ["ATProtoNet.Lexicon.App.Bsky.Feed.FeedClient.EnumerateSearchPostsAsync(since)"] =
            "No Lexicon format: a datetime or a bare ISO date (YYYY-MM-DD).",
        ["ATProtoNet.Lexicon.App.Bsky.Feed.FeedClient.EnumerateSearchPostsAsync(until)"] =
            "No Lexicon format: a datetime or a bare ISO date (YYYY-MM-DD).",
        ["ATProtoNet.Lexicon.Com.AtProto.Admin.SendEmailRequest.Subject"] =
            "The email's subject line.",
        ["ATProtoNet.Lexicon.Com.AtProto.Server.InviteCode.ForAccount"] =
            "No Lexicon format: the PDS writes `admin` for codes minted by the administrator.",
        ["ATProtoNet.Lexicon.Com.AtProto.Server.InviteCode.CreatedBy"] =
            "No Lexicon format: the PDS writes `admin` for codes minted by the administrator.",
        ["ATProtoNet.Lexicon.Com.AtProto.Server.AccountCodes.Account"] =
            "No Lexicon format: the PDS writes `admin` for codes minted by the administrator.",
        ["ATProtoNet.Lexicon.Com.AtProto.Admin.AdminClient.DisableInviteCodesAsync(accounts)"] =
            "No Lexicon format: the accounts whose codes to disable, including `admin`.",
        ["ATProtoNet.AtProtoClient.SetLabelers(labelerDids)"] =
            "Header entries: a labeler DID, optionally followed by the `;redact` parameter.",

        ["ATProtoNet.Lexicon.Chat.Bsky.Convo.ConvoView.Rev"] = ChatRev,
        ["ATProtoNet.Lexicon.Chat.Bsky.Convo.MessageView.Rev"] = ChatRev,
        ["ATProtoNet.Lexicon.Chat.Bsky.Convo.DeletedMessageView.Rev"] = ChatRev,
        ["ATProtoNet.Lexicon.Chat.Bsky.Convo.ConvoLogEntry.Rev"] = ChatRev,
        ["ATProtoNet.Lexicon.Chat.Bsky.Convo.LeaveConvoResponse.Rev"] = ChatRev,
        ["ATProtoNet.Lexicon.Chat.Bsky.Convo.AcceptConvoResponse.Rev"] = ChatRev,

        ["ATProtoNet.Lexicon.Tools.Ozone.Moderation.ModerationClient.QueryEventsAsync(subject)"] = OzoneSubjectFilter,
        ["ATProtoNet.Lexicon.Tools.Ozone.Moderation.ModerationClient.EnumerateEventsAsync(subject)"] = OzoneSubjectFilter,
        ["ATProtoNet.Lexicon.Tools.Ozone.Moderation.ModerationClient.QuerySubjectsAsync(subject)"] = OzoneSubjectFilter,
        ["ATProtoNet.Lexicon.Tools.Ozone.Moderation.ModerationClient.EnumerateSubjectsAsync(subject)"] = OzoneSubjectFilter,
        ["ATProtoNet.Lexicon.Tools.Ozone.Communication.CommunicationTemplateView.Subject"] =
            "The email's subject line.",
        ["ATProtoNet.Lexicon.Tools.Ozone.Communication.CreateTemplateRequest.Subject"] =
            "The email's subject line.",
        ["ATProtoNet.Lexicon.Tools.Ozone.Communication.UpdateTemplateRequest.Subject"] =
            "The email's subject line.",
        ["ATProtoNet.Lexicon.Tools.Ozone.Team.TeamMember.LastUpdatedBy"] =
            "No Lexicon format: Ozone writes `admin_token` when an admin token made the change.",
    };

    private const string ChatRev = "No Lexicon format: an opaque revision string of the chat service.";

    private const string OzoneSubjectFilter =
        "Lexicon format `uri`: an account subject is a bare DID, a record subject an AT URI.";

    // The last camel-case word of a member name that marks an identifier-carrying member, from
    // the names the upstream Lexicons give their identifier-format fields.
    private static readonly HashSet<string> IdentifierWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // did, handle, at-identifier
        "did", "dids", "handle", "handles", "actor", "actors", "repo", "src", "by", "viewer",
        "account", "accounts", "member", "members", "issuer", "issuers",
        // at-uri
        "uri", "uris", "subject", "subjects", "list", "feed", "post", "like", "repost",
        // cid
        "cid", "cids", "commit", "head", "prev", "root",
        // nsid
        "nsid", "collection", "collections", "lxm",
        // record-key, tid
        "rkey", "rkeys", "rev", "since",
        // datetime: createdAt, indexedAt, createdAfter, …
        "at", "after", "before", "until", "time", "cts", "exp",
    };

    [Fact]
    public void PublicSurface_IdentifierMembers_AreTyped()
    {
        var violations = FindViolations()
            .Where(v => !Exceptions.ContainsKey(v) && !IsPending(v))
            .ToList();

        Assert.True(
            violations.Count == 0,
            "These public members carry an identifier format as a string. Type them (Did, Handle, " +
            "AtIdentifier, AtUri, Nsid, Cid, RecordKey, Tid, AtDatetime), or add a commented " +
            "entry to TypedIdentifierGuardTests.Exceptions if the value genuinely is not one:\n  " +
            string.Join("\n  ", violations));
    }

    [Fact]
    public void Exceptions_AllStillMatchAMember()
    {
        var violations = FindViolations().ToHashSet(StringComparer.Ordinal);
        var stale = Exceptions.Keys.Where(key => !violations.Contains(key)).ToList();

        Assert.True(stale.Count == 0, "Remove these stale exceptions:\n  " + string.Join("\n  ", stale));
    }

    [Fact]
    public void PendingConversions_AllStillHaveViolations()
    {
        var violations = FindViolations().ToList();
        var done = PendingConversions
            .Where(prefix => !violations.Any(v => v.StartsWith(prefix, StringComparison.Ordinal)))
            .ToList();

        Assert.True(
            done.Count == 0,
            "These areas have no string identifiers left; remove them from PendingConversions:\n  " +
            string.Join("\n  ", done));
    }

    [Theory]
    [InlineData("did", true)]
    [InlineData("labelerDids", true)]
    [InlineData("CreatedAt", true)]
    [InlineData("recordUri", true)]
    [InlineData("swapCommit", true)]
    [InlineData("uriPatterns", false)]
    [InlineData("DidDoc", false)]
    [InlineData("Format", false)]
    [InlineData("Chat", false)]
    public void IsIdentifierName_ClassifiesByLastWord(string name, bool expected) =>
        Assert.Equal(expected, IsIdentifierName(name));

    private static bool IsPending(string member) =>
        PendingConversions.Any(prefix => member.StartsWith(prefix, StringComparison.Ordinal));

    private static IEnumerable<string> FindViolations()
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        foreach (var type in Assemblies.SelectMany(a => a.GetExportedTypes()).Where(IsScanned))
        {
            var typeName = type.FullName!.Replace('+', '.');

            foreach (var property in type.GetProperties(Declared))
            {
                if (IsIdentifierName(property.Name) && IsStringLike(property.PropertyType))
                    yield return $"{typeName}.{property.Name}";
            }

            var methods = type.GetMethods(Declared).Where(m => !m.IsSpecialName).Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.Instance));
            foreach (var method in methods)
            {
                foreach (var parameter in method.GetParameters())
                {
                    if (parameter.Name is { } name && IsIdentifierName(name) && IsStringLike(parameter.ParameterType))
                        yield return $"{typeName}.{(method.IsConstructor ? type.Name : method.Name)}({name})";
                }
            }
        }
    }

    private static bool IsScanned(Type type) =>
        type.Namespace is { } ns
        && ScannedNamespaces.Any(scope => scope.EndsWith(".*", StringComparison.Ordinal)
            ? ns == scope[..^2] || ns.StartsWith(scope[..^1], StringComparison.Ordinal)
            : ns == scope);

    private static bool IsIdentifierName(string name)
    {
        var words = Regex.Matches(name, "[A-Z]?[a-z0-9]+|[A-Z]+(?![a-z])");
        return words.Count > 0 && IdentifierWords.Contains(words[^1].Value);
    }

    private static bool IsStringLike(Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType()!;

        return type == typeof(string) || typeof(IEnumerable<string>).IsAssignableFrom(type);
    }
}
