using Flit.Tramites.Domain.RuntConfirmation;

namespace Flit.Tramites.Application.UseCases.RuntConfirmation;

/// <summary>Acceso a la fila única de <c>tramites.runt_confirmation_settings</c>.</summary>
public interface IRuntConfirmationSettingsRepository
{
    /// <summary>Devuelve la configuración vigente; si la fila aún no existe, los valores por defecto (sin persistirlos).</summary>
    Task<RuntConfirmationSettings> GetAsync(CancellationToken ct = default);

    /// <summary>Crea o actualiza la fila única. Nunca puede producir una segunda fila.</summary>
    Task SaveAsync(RuntConfirmationSettings settings, CancellationToken ct = default);
}

/// <summary>Un campo de la configuración que cambió: lo que se audita (HU #12277 AC3).</summary>
public sealed record RuntConfirmationSettingChange(string Field, string? OldValue, string? NewValue);

/// <summary>
/// Puerto de auditoría de la configuración. Lo implementa Infrastructure sobre el rastro
/// administrativo unificado (<c>IAdminAuditWriter</c> → <c>admin.tenant_config_audit_logs</c>), el mismo
/// que usan Notificaciones y FUR: no se inventa tabla de auditoría nueva.
/// </summary>
public interface IRuntConfirmationAuditWriter
{
    Task WriteSettingChangeAsync(RuntConfirmationSettingChange change, Guid? actorUserId, CancellationToken ct = default);
}
