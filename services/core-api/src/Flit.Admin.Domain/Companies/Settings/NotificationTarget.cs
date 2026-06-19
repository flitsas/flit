namespace Flit.Admin.Domain.Companies.Settings;

/// <summary>
/// Destinatario de las notificaciones operativas del tenant (RF09).
/// Valores válidos (API y BD, en mayúsculas): <c>COMPRADOR</c> | <c>RADICADOR</c> | <c>NINGUNO</c>.
/// </summary>
public enum NotificationTarget
{
    /// <summary>Notificar al comprador.</summary>
    Comprador = 0,

    /// <summary>Notificar al radicador del trámite.</summary>
    Radicador = 1,

    /// <summary>No notificar.</summary>
    Ninguno = 2,
}
