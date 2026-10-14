using Flit.Admin.Domain.Companies.Settings;
using Flit.Modules.Security.Domain.Auth;

namespace Flit.Infrastructure.Notifications.Admin;

/// <summary>Un canal de correo del ambiente, tal como lo tiene configurado Notificaciones.</summary>
/// <param name="Disponible">Se puede enviar por él (el canal de la empresa solo con su API habilitada).</param>
/// <param name="Consola">FLIT sin servidor SMTP: el correo se escribe en el log en vez de enviarse.</param>
public sealed record CanalDeNotificacion(NotificationChannel Canal, bool Disponible, string? RemitenteEmail, string? RemitenteNombre, bool Consola);

/// <summary>
/// HU #13359 (Epic #13316): lo que las pantallas del SuperAdmin necesitan de los transportes de correo, que ya no están en
/// core-api: los canales del ambiente (remitente y disponibilidad) y el envío del buzón de pruebas, que necesita la
/// respuesta en el momento. Lo implementa core-api con gRPC hacia core-notificaciones.
/// </summary>
public interface ICanalesDeNotificaciones
{
    /// <exception cref="NotificacionesNoDisponibleException">Notificaciones no respondió.</exception>
    Task<IReadOnlyList<CanalDeNotificacion>> ListarAsync(CancellationToken ct);

    /// <summary>Envía al buzón de pruebas (destinatario controlado: no se desvía). Nunca lanza por el transporte.</summary>
    Task<EmailSendResult> EnviarPruebaAsync(NotificationChannel canal, EmailMessage mensaje, CancellationToken ct);
}

/// <summary>Notificaciones no respondió (servicio caído o sin token).</summary>
public sealed class NotificacionesNoDisponibleException(string message, Exception inner) : Exception(message, inner);
