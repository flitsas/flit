namespace Flit.Tramites.Application.Notifications;

/// <summary>
/// Resuelve la variante FLIT/Renting del correo de asignación de placa según el canal de
/// notificaciones del tenant cliente (<c>flit_smtp</c> → FLIT, <c>tenant_api</c> → Renting),
/// alineado con ADR-0045 (aprobado/rechazado).
/// </summary>
public interface IPlateAssignmentBrandResolver
{
    /// <summary>
    /// Resuelve la marca del cuerpo a partir de <c>notification_channel</c> del tenant cliente.
    /// </summary>
    Task<PlateAssignmentEmailBrand> ResolveForClientTenantAsync(
        Guid clientTenantId,
        CancellationToken cancellationToken = default);
}
