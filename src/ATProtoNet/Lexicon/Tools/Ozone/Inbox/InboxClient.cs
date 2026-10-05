using ATProtoNet.Http;
using ATProtoNet.Lexicon.Com.AtProto.Moderation;

namespace ATProtoNet.Lexicon.Tools.Ozone.Inbox;

/// <summary>Client for tools.ozone.inbox.* endpoints: what an account can do about the moderation actions taken against it or its content. The caller is the affected account, not a moderator.</summary>
public sealed class InboxClient
{
    private readonly XrpcClient _xrpc;

    internal InboxClient(XrpcClient xrpc) => _xrpc = xrpc;

    /// <summary>Appeal a moderation action against the caller's account or content.</summary>
    /// <param name="subject">The subject: a <see cref="RepoSubject"/> or a <see cref="RecordSubject"/>.</param>
    /// <param name="action">The action to appeal: an <see cref="AppealActionRef"/>, <see cref="AppealLabelRef"/> or <see cref="AppealTakedownRef"/>.</param>
    /// <param name="reason">An explanation (at most 2000 graphemes).</param>
    /// <param name="modTool">The tool the appeal was filed with.</param>
    /// <exception cref="XrpcException"><c>InvalidAppealSubject</c>, <c>AlreadyAppealed</c>, <c>NotAppealable</c> or <c>AppealWindowExpired</c>.</exception>
    public Task<SubjectView> AppealActionedSubjectAsync(
        ModerationSubject subject,
        AppealAction? action = null,
        string? reason = null,
        ModTool? modTool = null,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<SubjectView>(
            "tools.ozone.inbox.appealActionedSubject",
            new AppealActionedSubjectRequest(subject, action, reason, modTool),
            cancellationToken: cancellationToken);
}
