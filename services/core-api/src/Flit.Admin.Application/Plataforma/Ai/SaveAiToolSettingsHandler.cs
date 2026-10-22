using Flit.Admin.Application.Auditing;
using Flit.Admin.Domain.Plataforma.Ai;

namespace Flit.Admin.Application.Plataforma.Ai;

public sealed class SaveAiToolSettingsHandler
{
    private readonly IAiToolProviderSettingsRepository _repository;
    private readonly IAiApiKeyEnvironmentProbe _keyProbe;
    private readonly IAdminAuditWriter _auditWriter;

    public SaveAiToolSettingsHandler(
        IAiToolProviderSettingsRepository repository,
        IAiApiKeyEnvironmentProbe keyProbe,
        IAdminAuditWriter auditWriter)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _keyProbe = keyProbe ?? throw new ArgumentNullException(nameof(keyProbe));
        _auditWriter = auditWriter ?? throw new ArgumentNullException(nameof(auditWriter));
    }

    public async Task<SaveAiToolSettingsResult> HandleAsync(
        string toolCode,
        SaveAiToolSettingsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!AiToolCodes.IsKnown(toolCode))
        {
            return new SaveAiToolSettingsResult(false, null, "herramienta_desconocida");
        }

        var validation = ValidateProviders(command.Providers);
        if (validation is not null)
        {
            return new SaveAiToolSettingsResult(false, null, validation);
        }

        var normalizedTool = AiToolCodes.Normalize(toolCode);
        var before = await _repository.GetByToolCodeAsync(normalizedTool, cancellationToken).ConfigureAwait(false);
        if (before is null)
        {
            return new SaveAiToolSettingsResult(false, null, "herramienta_sin_configuracion");
        }

        var slots = command.Providers
            .Select(p => new AiProviderSlot(AiProviderCodes.Normalize(p.ProviderCode), p.Enabled, p.PriorityRank))
            .ToList();

        var saved = await _repository
            .SaveSettingsAsync(
                normalizedTool,
                command.AutoReturnToPrimary,
                slots,
                command.ActorUserId,
                cancellationToken)
            .ConfigureAwait(false);

        if (saved is null)
        {
            return new SaveAiToolSettingsResult(false, null, "herramienta_sin_configuracion");
        }

        var view = AiToolSettingsMapper.ToView(saved, _keyProbe);

        await _auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                Module: AuditVocabulary.Modules.Config,
                EntityName: "ai_tool_provider_settings",
                Operation: AuditVocabulary.Operations.Update,
                Result: AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: command.ActorUserId,
                TargetEntityType: "AI_TOOL",
                TargetEntityId: null,
                ClientIp: null,
                UserAgent: null,
                OldValue: SerializeSummary(before),
                NewValue: SerializeSummary(saved)),
            cancellationToken).ConfigureAwait(false);

        return new SaveAiToolSettingsResult(true, view, null);
    }

    private static string? ValidateProviders(IReadOnlyList<AiProviderSlotCommand> providers)
    {
        if (providers is null || providers.Count == 0)
        {
            return "proveedores_invalidos";
        }

        var ranks = new HashSet<int>();
        foreach (var p in providers)
        {
            if (!AiProviderCodes.IsKnown(p.ProviderCode))
            {
                return "proveedor_desconocido";
            }

            if (p.PriorityRank < 1)
            {
                return "prioridad_invalida";
            }

            if (!ranks.Add(p.PriorityRank))
            {
                return "prioridad_duplicada";
            }
        }

        return null;
    }

    private static string SerializeSummary(AiToolProviderSettings settings)
    {
        var providers = string.Join(
            ",",
            settings.Providers.Select(p => $"{p.ProviderCode}:{p.Enabled}:{p.PriorityRank}"));
        return $"tool={settings.ToolCode};active={settings.ActiveProvider};autoReturn={settings.AutoReturnToPrimary};providers=[{providers}]";
    }
}
