using ATProtoNet.Http;

namespace ATProtoNet.Lexicon.Tools.Ozone.Communication;

/// <summary>
/// Client for tools.ozone.communication.* endpoints.
/// </summary>
public sealed class CommunicationClient
{
    private readonly XrpcClient _xrpc;

    internal CommunicationClient(XrpcClient xrpc)
    {
        _xrpc = xrpc;
    }

    /// <summary>
    /// Create a new email template.
    /// </summary>
    public Task<CommunicationTemplateView> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<CreateTemplateRequest, CommunicationTemplateView>(
            "tools.ozone.communication.createTemplate", request, cancellationToken: cancellationToken);

    /// <summary>
    /// Delete a communication template.
    /// </summary>
    public async Task DeleteTemplateAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        var request = new DeleteTemplateRequest { Id = id };
        await _xrpc.ProcedureAsync<DeleteTemplateRequest>(
            "tools.ozone.communication.deleteTemplate", request, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// List all communication templates.
    /// </summary>
    public Task<ListTemplatesResponse> ListTemplatesAsync(
        CancellationToken cancellationToken = default) =>
        _xrpc.QueryAsync<ListTemplatesResponse>(
            "tools.ozone.communication.listTemplates", cancellationToken: cancellationToken);

    /// <summary>
    /// Update an existing communication template.
    /// </summary>
    public Task<CommunicationTemplateView> UpdateTemplateAsync(
        UpdateTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        _xrpc.ProcedureAsync<UpdateTemplateRequest, CommunicationTemplateView>(
            "tools.ozone.communication.updateTemplate", request, cancellationToken: cancellationToken);
}
