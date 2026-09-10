using System.Text.Json;
using Flit.Admin.Application.Auditing;
using Flit.Tramites.Application.UseCases.RuntConfirmation;

namespace Flit.Infrastructure.RuntConfirmation;

/// <summary>
/// Auditoría de la configuración de Confirmación RUNT sobre el rastro administrativo unificado
/// (<c>admin.tenant_config_audit_logs</c> vía <see cref="IAdminAuditWriter"/>), el mismo mecanismo
/// que usan Notificaciones y FUR. Una fila por campo cambiado: el campo va en <c>old_value</c> /
/// <c>new_value</c> como <c>{"field": …, "value": …}</c>, el actor en <c>changed_by</c> y la fecha en
/// <c>changed_at</c> (HU #12277 AC3). Global de plataforma: sin tenant.
/// </summary>
internal sealed class RuntConfirmationAuditWriter(
    IAdminAuditWriter auditWriter,
    IAuditContextAccessor auditContext) : IRuntConfirmationAuditWriter
{
    public const string EntityName = "runt_confirmation_settings";
    public const string TargetEntityType = "RUNT_CONFIRMATION_SETTINGS";

    public Task WriteSettingChangeAsync(RuntConfirmationSettingChange change, Guid? actorUserId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        return auditWriter.WriteAsync(
            new AdminAuditEntry(
                TenantId: null,
                TenantType: null,
                AuditVocabulary.Modules.Config,
                EntityName,
                AuditVocabulary.Operations.Update,
                AuditVocabulary.Results.Success,
                ErrorCode: null,
                ActorUserId: actorUserId ?? auditContext.UserId,
                TargetEntityType,
                TargetEntityId: null,
                auditContext.ClientIp,
                UserAgent: null,
                OldValue: Serialize(change.Field, change.OldValue),
                NewValue: Serialize(change.Field, change.NewValue)),
            ct);
    }

    private static string Serialize(string field, string? value) =>
        JsonSerializer.Serialize(new Dictionary<string, string?> { ["field"] = field, ["value"] = value });
}
