namespace Flit.Admin.Application.Companies.Settings.UpdateTenantSettings;

/// <summary>
/// Comando de actualización atómica de la configuración operativa del tenant
/// (AC1). Combina el <see cref="TenantId"/> de ruta con el payload y la identidad
/// del SuperAdmin que ejecuta el cambio (para la auditoría).
/// </summary>
public sealed class UpdateTenantSettingsCommand
{
    public required Guid TenantId { get; init; }

    public required UpdateTenantSettingsRequest Request { get; init; }

    /// <summary>Id del usuario (claim <c>sub</c> del JWT) que realiza el cambio. Opcional.</summary>
    public Guid? ChangedBy { get; init; }

    /// <summary>Id de correlación opcional para trazabilidad de la auditoría.</summary>
    public Guid? CorrelationId { get; init; }
}
