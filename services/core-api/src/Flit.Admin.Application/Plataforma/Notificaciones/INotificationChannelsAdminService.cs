namespace Flit.Admin.Application.Plataforma.Notificaciones;

/// <summary>
/// Vista de un canal de envío de notificaciones con su remitente ya resuelto (HU #11367,
/// Feature #11349). El remitente se lee de configuración — NO se construye ningún adaptador de
/// envío aquí (eso es la HU #11360/#11361, Feature #11348).
/// </summary>
/// <param name="Channel">Código wire del canal: <c>FLIT_SMTP</c> | <c>TENANT_API</c> (ver <c>SettingsWire</c>).</param>
/// <param name="Label">Etiqueta de producto: "Colas FLIT" | "API Renting cliente".</param>
/// <param name="IsDefault">
/// <c>true</c> únicamente para <c>FLIT_SMTP</c> (AC1) — es el canal por defecto de la plataforma.
/// </param>
/// <param name="IsConfigured">
/// <c>false</c> cuando el canal no tiene remitente definido en configuración (AC3) — la pantalla
/// necesita distinguir "sin configurar" de una respuesta con error.
/// </param>
/// <param name="SenderEmail">Correo remitente del canal, o <c>null</c> si no está configurado.</param>
/// <param name="SenderName">Nombre/usuario remitente del canal, o <c>null</c> si no está configurado.</param>
public sealed record NotificationChannelView(
    string Channel,
    string Label,
    bool IsDefault,
    bool IsConfigured,
    string? SenderEmail,
    string? SenderName);

/// <summary>
/// Lectura de los canales de envío de notificaciones disponibles en la plataforma, con su
/// remitente resuelto por configuración. Alcance EXACTO de la HU #11367: solo lectura — nada de
/// envío de prueba (HU #11368) ni adaptadores de transporte (HU #11360/#11361).
/// </summary>
public interface INotificationChannelsAdminService
{
    Task<IReadOnlyList<NotificationChannelView>> GetAsync(CancellationToken ct = default);
}
