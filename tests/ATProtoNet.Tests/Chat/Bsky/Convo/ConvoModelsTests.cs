using System.Text.Json;
using ATProtoNet.Identity;
using ATProtoNet.Lexicon.App.Bsky.Embed;
using ATProtoNet.Lexicon.App.Bsky.RichText;
using ATProtoNet.Lexicon.Chat.Bsky.Actor;
using ATProtoNet.Lexicon.Chat.Bsky.Convo;
using ATProtoNet.Lexicon.Chat.Bsky.Embed;
using ATProtoNet.Lexicon.Chat.Bsky.Group;
using ATProtoNet.Serialization;
using ATProtoNet.Tests.Lexicon.Upstream;

namespace ATProtoNet.Tests.Chat.Bsky.Convo;

/// <summary>
/// The typed chat views of group chats: conversation and member kinds, messages with replies,
/// reactions and embeds, system messages and the log. The chat service is not open source, so the
/// fixtures follow the vendored <c>chat.bsky.*</c> Lexicons field by field.
/// </summary>
public class ConvoModelsTests
{
    private static readonly JsonSerializerOptions Options = AtProtoJsonDefaults.Options;

    private const string Owner = """{"did":"did:plc:owner","handle":"owner.bsky.social","displayName":"Owner"}""";

    private const string Alice = """{"did":"did:plc:alice","handle":"alice.bsky.social"}""";

    private const string Message =
        """
        {"$type":"chat.bsky.convo.defs#messageView","id":"msg-1","rev":"2222222222223","text":"hello",
         "sender":{"did":"did:plc:alice"},"sentAt":"2026-06-01T12:01:00.000Z"}
        """;

    private const string DeletedMessage =
        """
        {"$type":"chat.bsky.convo.defs#deletedMessageView","id":"msg-0","rev":"2222222222222",
         "sender":{"did":"did:plc:owner"},"sentAt":"2026-06-01T12:00:30.000Z"}
        """;

    private const string SystemMessage =
        """
        {"$type":"chat.bsky.convo.defs#systemMessageView","id":"msg-2","rev":"2222222222224","sentAt":"2026-06-01T12:02:00.000Z",
         "data":{"$type":"chat.bsky.convo.defs#systemMessageDataAddMember","member":{"did":"did:plc:alice"},"role":"standard",
                 "addedBy":{"did":"did:plc:owner"}}}
        """;

    private const string Reaction =
        """{"value":"👍","sender":{"did":"did:plc:owner"},"createdAt":"2026-06-01T12:03:00.000Z"}""";

    private const string GroupConvoView =
        $$$"""
        {"id":"convo-1","rev":"2222222222225",
         "members":[
           {"did":"did:plc:owner","handle":"owner.bsky.social","displayName":"Owner",
            "kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"owner"}},
           {"did":"did:plc:alice","handle":"alice.bsky.social","createdAt":"2024-01-01T00:00:00.000Z",
            "kind":{"$type":"chat.bsky.actor.defs#groupConvoMember","role":"standard","addedBy":{{{Owner}}}}}],
         "lastMessage":{{{SystemMessage}}},
         "lastReaction":{"$type":"chat.bsky.convo.defs#messageAndReactionView","message":{{{Message}}},"reaction":{{{Reaction}}}},
         "muted":false,"status":"accepted","unreadCount":2,
         "kind":{"$type":"chat.bsky.convo.defs#groupConvo","name":"Book club","memberCount":2,"memberLimit":100,
                 "lockStatus":"unlocked","lockStatusModerationOverride":false,"createdAt":"2026-06-01T12:00:00.000Z",
                 "joinLink":{"code":"abc123","enabledStatus":"enabled","requireApproval":true,"joinRule":"followedByOwner",
                             "createdAt":"2026-06-01T12:00:10.000Z"},
                 "joinRequestCount":3,"unreadJoinRequestCount":1}}
        """;

    // ──────────────────────────────────────────────────────────
    //  Conversations and members
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_GroupConvoView_ReadsTheGroupMembersAndLatestActivity()
    {
        var convo = JsonSerializer.Deserialize<ConvoView>(GroupConvoView, Options)!;

        var group = Assert.IsType<GroupConvo>(convo.Kind);
        Assert.Equal(("Book club", 2, 100), (group.Name, group.MemberCount, group.MemberLimit));
        Assert.Equal(ConvoLockStatus.Unlocked, group.LockStatus);
        Assert.False(group.LockStatusModerationOverride);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:00:00.000Z"), group.CreatedAt);
        Assert.Equal((3, 1), (group.JoinRequestCount, group.UnreadJoinRequestCount));
        Assert.Equal("abc123", group.JoinLink!.Code);
        Assert.Equal(JoinLinkEnabledStatus.Enabled, group.JoinLink.EnabledStatus);
        Assert.Equal(JoinRule.FollowedByOwner, group.JoinLink.JoinRule);
        Assert.True(group.JoinLink.RequireApproval);

        Assert.Equal(ChatMemberRole.Owner, Assert.IsType<GroupConvoMember>(convo.Members[0].Kind).Role);
        var alice = Assert.IsType<GroupConvoMember>(convo.Members[1].Kind);
        Assert.Equal(Handle.Parse("owner.bsky.social"), alice.AddedBy!.Handle);

        var system = Assert.IsType<SystemMessageView>(convo.LastMessage);
        var added = Assert.IsType<SystemMessageDataAddMember>(system.Data);
        Assert.Equal((Did.Parse("did:plc:alice"), ChatMemberRole.Standard, Did.Parse("did:plc:owner")),
            (added.Member.Did, added.Role, added.AddedBy.Did));

        var lastReaction = Assert.IsType<MessageAndReactionView>(convo.LastReaction);
        Assert.Equal("hello", lastReaction.Message.Text);
        Assert.Equal("👍", lastReaction.Reaction.Value);
        Assert.Equal(ConvoStatus.Accepted, convo.Status);
    }

    [Fact]
    public void Serialize_GroupConvoView_RoundTripsEveryField()
    {
        var original = JsonDocument.Parse(GroupConvoView).RootElement;

        var written = JsonSerializer.SerializeToElement(original.Deserialize<ConvoView>(Options), Options);

        // The view is itself a union variant (of listConvoRequests), so it writes its $type.
        Assert.Equal("chat.bsky.convo.defs#convoView", written.GetProperty("$type").GetString());
        var withoutType = JsonSerializer.SerializeToElement(
            written.EnumerateObject().Where(p => p.Name != "$type").ToDictionary(p => p.Name, p => p.Value));
        AssertJsonEqual(original, withoutType);
    }

    [Fact]
    public void Deserialize_DirectConvoWithoutKind_LeavesKindNull()
    {
        const string json =
            """
            {"id":"convo-1","rev":"r","members":[],"muted":true,"unreadCount":0,"lastMessage":
            """ + DeletedMessage + "}";

        var convo = JsonSerializer.Deserialize<ConvoView>(json, Options)!;

        Assert.Null(convo.Kind);
        Assert.Equal("msg-0", Assert.IsType<DeletedMessageView>(convo.LastMessage).Id);
    }

    // ──────────────────────────────────────────────────────────
    //  Messages
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_MessageView_ReadsFacetsJoinLinkEmbedReactionsAndReply()
    {
        const string json =
            $$$"""
            {"id":"msg-3","rev":"2222222222226","text":"join @alice","sender":{"did":"did:plc:owner"},
             "sentAt":"2026-06-01T12:04:00.000Z",
             "facets":[{"index":{"byteStart":5,"byteEnd":11},
                        "features":[{"$type":"app.bsky.richtext.facet#mention","did":"did:plc:alice"}]}],
             "embed":{"$type":"chat.bsky.embed.joinLink#view",
                      "joinLinkPreview":{"$type":"chat.bsky.group.defs#joinLinkPreviewView","convoId":"convo-1","code":"abc123",
                                         "name":"Book club","owner":{{{Owner}}},"memberCount":2,"memberLimit":100,
                                         "viewer":{"requestedAt":"2026-06-01T12:05:00.000Z"},
                                         "requireApproval":true,"joinRule":"anyone"}},
             "reactions":[{{{Reaction}}},{"value":"❤️","sender":{"did":"did:plc:alice"},"createdAt":"2026-06-01T12:06:00.000Z"}],
             "replyTo":{"$type":"chat.bsky.convo.defs#messageBeforeUserJoinedGroupView"}}
            """;

        var message = JsonSerializer.Deserialize<MessageView>(json, Options)!;

        var mention = Assert.IsType<MentionFeature>(Assert.Single(Assert.Single(message.Facets!).Features));
        Assert.Equal(Did.Parse("did:plc:alice"), mention.Did);

        var preview = Assert.IsType<JoinLinkPreviewView>(Assert.IsType<JoinLinkEmbedView>(message.Embed).JoinLinkPreview);
        Assert.Equal(("convo-1", "abc123", "Book club"), (preview.ConvoId, preview.Code, preview.Name));
        Assert.Equal(JoinRule.Anyone, preview.JoinRule);
        Assert.Equal(Did.Parse("did:plc:owner"), preview.Owner.Did);
        Assert.Null(preview.Convo);
        Assert.Equal(AtDatetime.Parse("2026-06-01T12:05:00.000Z"), preview.Viewer!.RequestedAt);

        Assert.Equal(["👍", "❤️"], message.Reactions!.Select(r => r.Value));
        Assert.Equal(Did.Parse("did:plc:alice"), message.Reactions![1].Sender.Did);
        Assert.IsType<MessageBeforeUserJoinedGroupView>(message.ReplyTo);
    }

    [Fact]
    public void Deserialize_MessageView_ReadsAQuotedPostAndADeletedParent()
    {
        const string json =
            $$$"""
            {"id":"msg-4","rev":"2222222222227","text":"look","sender":{"did":"did:plc:alice"},"sentAt":"2026-06-01T12:07:00.000Z",
             "embed":{"$type":"app.bsky.embed.record#view",
                      "record":{"$type":"app.bsky.embed.record#viewNotFound",
                                "uri":"at://did:plc:alice/app.bsky.feed.post/3lq5a2kxzqc2a","notFound":true}},
             "replyTo":{{{DeletedMessage}}}}
            """;

        var message = JsonSerializer.Deserialize<MessageView>(json, Options)!;

        var record = Assert.IsType<MessageRecordEmbedView>(message.Embed);
        var notFound = Assert.IsType<EmbeddedRecordNotFound>(record.Record);
        Assert.Equal(AtUri.Parse("at://did:plc:alice/app.bsky.feed.post/3lq5a2kxzqc2a"), notFound.Uri);
        Assert.Equal("msg-0", Assert.IsType<DeletedMessageView>(message.ReplyTo).Id);
    }

    [Fact]
    public void Deserialize_GetMessagesPage_ReadsEveryMessageKindAndRelatedProfiles()
    {
        const string json =
            $$"""
            {"cursor":"c","messages":[{{Message}},{{DeletedMessage}},{{SystemMessage}}],
             "relatedProfiles":[{{Owner}},{{Alice}}]}
            """;

        var page = JsonSerializer.Deserialize<GetMessagesResponse>(json, Options)!;

        Assert.Collection(page.Messages,
            m => Assert.Equal("hello", Assert.IsType<MessageView>(m).Text),
            m => Assert.IsType<DeletedMessageView>(m),
            m => Assert.IsType<SystemMessageDataAddMember>(Assert.IsType<SystemMessageView>(m).Data));
        Assert.Equal(["owner.bsky.social", "alice.bsky.social"], page.RelatedProfiles!.Select(p => p.Handle.ToString()));
    }

    [Theory]
    [InlineData("systemMessageDataAddMember", ""","member":{"did":"did:plc:alice"},"role":"standard","addedBy":{"did":"did:plc:owner"}""", typeof(SystemMessageDataAddMember))]
    [InlineData("systemMessageDataRemoveMember", ""","member":{"did":"did:plc:alice"},"removedBy":{"did":"did:plc:owner"}""", typeof(SystemMessageDataRemoveMember))]
    [InlineData("systemMessageDataMemberJoin", ""","member":{"did":"did:plc:alice"},"role":"standard","approvedBy":{"did":"did:plc:owner"}""", typeof(SystemMessageDataMemberJoin))]
    [InlineData("systemMessageDataMemberLeave", ""","member":{"did":"did:plc:alice"}""", typeof(SystemMessageDataMemberLeave))]
    [InlineData("systemMessageDataLockConvo", ""","lockedBy":{"did":"did:plc:owner"}""", typeof(SystemMessageDataLockConvo))]
    [InlineData("systemMessageDataUnlockConvo", ""","unlockedBy":{"did":"did:plc:owner"}""", typeof(SystemMessageDataUnlockConvo))]
    [InlineData("systemMessageDataLockConvoPermanently", ""","lockedBy":{"did":"did:plc:owner"}""", typeof(SystemMessageDataLockConvoPermanently))]
    [InlineData("systemMessageDataEditGroup", ""","oldName":"Books","newName":"Book club" """, typeof(SystemMessageDataEditGroup))]
    [InlineData("systemMessageDataCreateJoinLink", "", typeof(SystemMessageDataCreateJoinLink))]
    [InlineData("systemMessageDataEditJoinLink", "", typeof(SystemMessageDataEditJoinLink))]
    [InlineData("systemMessageDataEnableJoinLink", "", typeof(SystemMessageDataEnableJoinLink))]
    [InlineData("systemMessageDataDisableJoinLink", "", typeof(SystemMessageDataDisableJoinLink))]
    public void Deserialize_SystemMessageData_ReadsEachVariantAndRoundTrips(string def, string fields, Type expected)
    {
        var json = $$"""{"$type":"chat.bsky.convo.defs#{{def}}"{{fields}}}""";

        var data = JsonSerializer.Deserialize<SystemMessageData>(json, Options)!;

        Assert.IsType(expected, data);
        Assert.Null(data.ExtensionData);
        AssertJsonEqual(JsonDocument.Parse(json).RootElement, JsonSerializer.SerializeToElement(data, Options));
    }

    [Fact]
    public void Deserialize_SystemMessageDataMemberJoin_ReadsWhoApproved()
    {
        const string json =
            """
            {"$type":"chat.bsky.convo.defs#systemMessageDataMemberJoin","member":{"did":"did:plc:alice"},
             "role":"standard","approvedBy":{"did":"did:plc:owner"}}
            """;

        var join = Assert.IsType<SystemMessageDataMemberJoin>(JsonSerializer.Deserialize<SystemMessageData>(json, Options));

        Assert.Equal(Did.Parse("did:plc:owner"), join.ApprovedBy!.Did);
    }

    // ──────────────────────────────────────────────────────────
    //  Log
    // ──────────────────────────────────────────────────────────

    private const string Profiles = $$""","relatedProfiles":[{{Owner}},{{Alice}}]""";

    private const string WithMessage = $$""","message":{{Message}}""";

    private const string WithSystemMessage = $$""","message":{{SystemMessage}}""";

    private const string WithMember = $$""","member":{{Alice}}""";

    private static readonly (string Def, string Fields, Type Expected)[] LogEntryCases =
    [
        ("logBeginConvo", "", typeof(LogBeginConvo)),
        ("logAcceptConvo", "", typeof(LogAcceptConvo)),
        ("logLeaveConvo", "", typeof(LogLeaveConvo)),
        ("logMuteConvo", "", typeof(LogMuteConvo)),
        ("logUnmuteConvo", "", typeof(LogUnmuteConvo)),
        ("logCreateMessage", WithMessage + Profiles, typeof(LogCreateMessage)),
        ("logDeleteMessage", $$""","message":{{DeletedMessage}}""", typeof(LogDeleteMessage)),
        ("logReadMessage", WithMessage, typeof(LogReadMessage)),
        ("logAddReaction", WithMessage + $$""","reaction":{{Reaction}}""" + Profiles, typeof(LogAddReaction)),
        ("logRemoveReaction", WithMessage + $$""","reaction":{{Reaction}}""", typeof(LogRemoveReaction)),
        ("logReadConvo", WithSystemMessage, typeof(LogReadConvo)),
        ("logAddMember", WithSystemMessage + Profiles, typeof(LogAddMember)),
        ("logRemoveMember", WithSystemMessage + Profiles, typeof(LogRemoveMember)),
        ("logMemberJoin", WithSystemMessage + Profiles, typeof(LogMemberJoin)),
        ("logMemberLeave", WithSystemMessage + Profiles, typeof(LogMemberLeave)),
        ("logLockConvo", WithSystemMessage + Profiles, typeof(LogLockConvo)),
        ("logUnlockConvo", WithSystemMessage + Profiles, typeof(LogUnlockConvo)),
        ("logLockConvoPermanently", WithSystemMessage + Profiles, typeof(LogLockConvoPermanently)),
        ("logEditGroup", WithSystemMessage, typeof(LogEditGroup)),
        ("logCreateJoinLink", WithSystemMessage, typeof(LogCreateJoinLink)),
        ("logEditJoinLink", WithSystemMessage, typeof(LogEditJoinLink)),
        ("logEnableJoinLink", WithSystemMessage, typeof(LogEnableJoinLink)),
        ("logDisableJoinLink", WithSystemMessage, typeof(LogDisableJoinLink)),
        ("logIncomingJoinRequest", WithMember, typeof(LogIncomingJoinRequest)),
        ("logApproveJoinRequest", WithMember, typeof(LogApproveJoinRequest)),
        ("logRejectJoinRequest", WithMember, typeof(LogRejectJoinRequest)),
        ("logOutgoingJoinRequest", "", typeof(LogOutgoingJoinRequest)),
        ("logWithdrawIncomingJoinRequest", WithMember, typeof(LogWithdrawIncomingJoinRequest)),
        ("logWithdrawOutgoingJoinRequest", "", typeof(LogWithdrawOutgoingJoinRequest)),
        ("logReadJoinRequests", "", typeof(LogReadJoinRequests)),
    ];

    public static TheoryData<string, string, Type> LogEntries
    {
        get
        {
            var data = new TheoryData<string, string, Type>();
            foreach (var (def, fields, expected) in LogEntryCases)
                data.Add(def, fields, expected);
            return data;
        }
    }

    [Theory]
    [MemberData(nameof(LogEntries))]
    public void Deserialize_LogEntry_ReadsEachVariantAndRoundTrips(string def, string fields, Type expected)
    {
        var json = $$"""{"$type":"chat.bsky.convo.defs#{{def}}","rev":"2222222222229","convoId":"convo-1"{{fields}}}""";

        var entry = JsonSerializer.Deserialize<ConvoLogEntry>(json, Options)!;

        Assert.IsType(expected, entry);
        Assert.Equal(("2222222222229", "convo-1"), (entry.Rev, entry.ConvoId));
        Assert.Null(entry.ExtensionData);
        AssertJsonEqual(JsonDocument.Parse(json).RootElement, JsonSerializer.SerializeToElement(entry, Options));
    }

    [Fact]
    public void LogEntries_CoverEveryVariantOfTheGetLogUnion()
    {
        Assert.True(UpstreamLexicons.Instance.TryGetObject("chat.bsky.convo.getLog#output", out var output));
        var upstream = output.GetProperty("properties").GetProperty("logs").GetProperty("items").GetProperty("refs")
            .EnumerateArray().Select(r => r.GetString()!["chat.bsky.convo.defs#".Length..])
            .Order(StringComparer.Ordinal);

        var covered = LogEntryCases.Select(c => c.Def).Order(StringComparer.Ordinal);

        Assert.Equal(upstream, covered);
    }

    [Fact]
    public void Deserialize_LogAddReaction_ReadsTheMessageReactionAndProfiles()
    {
        var json =
            $$"""{"$type":"chat.bsky.convo.defs#logAddReaction","rev":"r","convoId":"convo-1"{{WithMessage}},"reaction":{{Reaction}}{{Profiles}}}""";

        var entry = Assert.IsType<LogAddReaction>(JsonSerializer.Deserialize<ConvoLogEntry>(json, Options));

        Assert.Equal("msg-1", Assert.IsType<MessageView>(entry.Message).Id);
        Assert.Equal("👍", entry.Reaction.Value);
        Assert.Equal(2, entry.RelatedProfiles!.Count);
    }

    // ──────────────────────────────────────────────────────────
    //  Unknown variants
    // ──────────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_UnknownLogEntry_KeepsTheObjectAndStillNamesItsConvo()
    {
        const string json =
            """{"logs":[{"convoId":"convo-9","$type":"chat.bsky.convo.defs#logPinMessage","rev":"r9","messageId":"msg-9"}]}""";

        var page = JsonSerializer.Deserialize<GetLogResponse>(json, Options)!;

        var unknown = Assert.IsType<UnknownConvoLogEntry>(Assert.Single(page.Logs));
        Assert.Equal("chat.bsky.convo.defs#logPinMessage", unknown.Type);
        Assert.Equal(("r9", "convo-9"), (unknown.Rev, unknown.ConvoId));
        Assert.Equal(
            """{"convoId":"convo-9","$type":"chat.bsky.convo.defs#logPinMessage","rev":"r9","messageId":"msg-9"}""",
            JsonSerializer.Serialize<ConvoLogEntry>(unknown, Options));
    }

    [Fact]
    public void UnknownConvoLogEntry_WithoutRevOrConvoId_LeavesThemEmpty()
    {
        var unknown = new UnknownConvoLogEntry("chat.bsky.convo.defs#logFuture", JsonDocument.Parse("""{"$type":"chat.bsky.convo.defs#logFuture"}""").RootElement);

        Assert.Equal(("", ""), (unknown.Rev, unknown.ConvoId));
    }

    public static TheoryData<Type, Type, string> UnknownVariants => new()
    {
        { typeof(ConvoKind), typeof(UnknownConvoKind), "chat.bsky.convo.defs#channelConvo" },
        { typeof(ChatMemberKind), typeof(UnknownChatMemberKind), "chat.bsky.actor.defs#guestConvoMember" },
        { typeof(ConvoMessage), typeof(UnknownConvoMessage), "chat.bsky.convo.defs#pollMessageView" },
        { typeof(ConvoLastReaction), typeof(UnknownConvoLastReaction), "chat.bsky.convo.defs#reactionOnlyView" },
        { typeof(SystemMessageData), typeof(UnknownSystemMessageData), "chat.bsky.convo.defs#systemMessageDataPin" },
        { typeof(ConvoRequestView), typeof(UnknownConvoRequestView), "chat.bsky.group.defs#inviteConvoView" },
        { typeof(MessageEmbed), typeof(UnknownMessageEmbed), "chat.bsky.embed.poll" },
        { typeof(MessageEmbedView), typeof(UnknownMessageEmbedView), "chat.bsky.embed.poll#view" },
        { typeof(JoinLinkPreview), typeof(UnknownJoinLinkPreview), "chat.bsky.group.defs#expiredJoinLinkPreviewView" },
    };

    [Theory]
    [MemberData(nameof(UnknownVariants))]
    public void Deserialize_UnknownVariant_KeepsTheRawObjectAndWritesItBackUnchanged(Type union, Type unknown, string type)
    {
        var json = $$"""{"extra":{"n":1.50},"$type":"{{type}}","note":"caf\u00e9"}""";

        var value = JsonSerializer.Deserialize(json, union, Options)!;

        Assert.IsType(unknown, value);
        Assert.Equal(type, ((IUnknownUnionVariant)value).Type);
        Assert.Equal(json, JsonSerializer.Serialize(value, union, Options));
    }

    private static void AssertJsonEqual(JsonElement expected, JsonElement actual) =>
        Assert.True(JsonElement.DeepEquals(expected, actual),
            $"expected {expected.GetRawText()}\nactual   {actual.GetRawText()}");
}
