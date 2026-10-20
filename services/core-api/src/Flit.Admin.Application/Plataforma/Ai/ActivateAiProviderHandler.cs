using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

public sealed class ActivateAiProviderHandler
{
    private readonly IAiToolProviderSettingsRepository _repository;
    private readonly IAiApiKeyEnvironmentProbe _keyProbe;
    private readonly IAdminAuditWriter _auditWriter;

    public ActivateAiProviderHandler(
        IAiToolProviderSettingsRepository repository,
        IAiApiKeyEnvironmentProbe keyProbe,
        IAdminAuditWriter auditWriter)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyProbe = keyProbe ?? throw new ArgumentNullException(nameof(keyProbe));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    }

    public async Task<ActivateAiProviderResult> HandleAsync(
        string toolCode,
        ActivateAiProviderCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AiToolCodes.IsKnown(toolCode))
        {
            return new ActivateAiProviderResult(ActivateAiProviderOutcome.ToolNotFound, null);
        }

        if (!AiProviderCodes.IsKnown(command.ProviderCode))
        {
            return new ActivateAiProviderResult(ActivateAiProviderOutcome.UnknownProvider, null);
        }

        var normalizedTool = AiToolCodes.Normalize(toolCode);
        var normalizedProvider = AiProviderCodes.Normalize(command.ProviderCode);

        if (!AiProviderCapabilities.SupportsTool(normalizedTool, normalizedProvider))
        {
            return new ActivateAiProviderResult(ActivateAiProviderOutcome.ProviderNotCapable, null);
        }

        if (!_keyProbe.HasApiKey(normalizedProvider))
        {
            await WriteFailureAuditAsync(
                normalizedTool,
                normalizedProvider,
                "proveedor_sin_credencial",
                command.ActorUserId,
                cancellationToken).ConfigureAwait(false);

            return new ActivateAiProviderResult(ActivateAiProviderOutcome.ProviderNoApiKey, null);
        }

        var current = await _repository.GetByToolCodeAsync(normalizedTool, cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return new ActivateAiProviderResult(ActivateAiProviderOutcome.ToolNotFound, null);
        }

        var slot = current.Providers.FirstOrDefault(
            p => string.Equals(p.ProviderCode, normalizedProvider, StringComparison.Ordinal));
        if (slot is null)
        {
            return new ActivateAiProviderResult(ActivateAiProviderOutcome.UnknownProvider, null);
        }

        if (!slot.Enabled)
        {
            return new ActivateAiProviderResult(ActivateAiProviderOutcome.ProviderDisabled, null);
        }

        if (string.Equals(current.ActiveProvider, normalizedProvider, StringComparison.Ordinal))
        {
            var unchangedView = AiToolSettingsMapper.ToView(current, _keyProbe);
            return new ActivateAiProviderResult(ActivateAiProviderOutcome.Unchanged, unchangedView);
        }

        var (result, updated) = await _repository
            .ActivateProviderAsync(
                normalizedTool,
                normalizedProvider,
                command.ActorUserId,
                command.Reason,
                cancellationToken)
            .ConfigureAwait(false);

        var outcome = result switch
        {
            ActivateProviderResult.Ok => ActivateAiProviderOutcome.Ok,
            ActivateProviderResult.Unchanged => ActivateAiProviderOutcome.Unchanged,
            ActivateProviderResult.ToolNotFound => ActivateAiProviderOutcome.ToolNotFound,
            ActivateProviderResult.UnknownProvider => ActivateAiProviderOutcome.UnknownProvider,
            ActivateProviderResult.ProviderDisabled => ActivateAiProviderOutcome.ProviderDisabled,
            ActivateProviderResult.ProviderNotCapable => ActivateAiProviderOutcome.ProviderNotCapable,
            ActivateProviderResult.ProviderNoApiKey => ActivateAiProviderOutcome.ProviderNoApiKey,
            _ => ActivateAiProviderOutcome.ToolNotFound,
        };

        if (outcome is ActivateAiProviderOutcome.Ok or ActivateAiProviderOutcome.Unchanged && updated is not null)
        {
            var view = AiToolSettingsMapper.ToView(updated, _keyProbe);
            if (outcome == ActivateAiProviderOutcome.Ok)
            {
                await _auditWriter.WriteAsync(
                    new AdminAuditEntry(
                        TenantId: null,
                        TenantType: null,
                        Module: AuditVocabulary.Modules.Config,
                        EntityName: "ai_tool_provider_settings",
                        Operation: AuditVocabulary.Operations.Activate,
                        Result: AuditVocabulary.Results.Success,
                        ErrorCode: null,
                        ActorUserId: command.ActorUserId,
                        TargetEntityType: "AI_TOOL",
                        TargetEntityId: null,
                        ClientIp: null,
                        UserAgent: null,
                        OldValue: $"activeProvider={current.ActiveProvider}",
                        NewValue: $"activeProvider={normalizedProvider}"),
                    cancellationToken).ConfigureAwait(false);
            }

            return new ActivateAiProviderResult(outcome, view);
        }

        return new ActivateAiProviderResult(outcome, null);
    }

    private Task WriteFailureAuditAsync(
        string toolCode,
        string providerCode,
        string errorCode,
        Guid? actorUserId,
        CancellationToken cancellationToken) =>
        _auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                EntityName: "ai_tool_provider_settings",
                Operation: AuditVocabulary.Operations.Activate,
                Result: AuditVocabulary.Results.Failure,
                ErrorCode: errorCode,
                ActorUserId: actorUserId,
                TargetEntityType: "AI_TOOL",
                TargetEntityId: null,
                ClientIp: null,
                UserAgent: null,
                OldValue: $"toolCode={toolCode}",
                NewValue: $"providerCode={providerCode}"),
            cancellationToken);
}
