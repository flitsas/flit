namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Canal de enrutamiento de notificaciones del tenant (RF09).
/// Contrato API (wire): <c>FLIT_SMTP</c> | <c>TENANT_API</c>.
/// Persistencia (BD): <c>flit_smtp</c> | <c>tenant_api</c>.
/// </summary>
public enum NotificationChannel
{
    /// <summary>SMTP gestionado por FLIT.</summary>
    FlitSmtp = 0,

    /// <summary>API/SMTP propio del tenant.</summary>
    TenantApi = 1,
}
