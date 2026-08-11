namespace Flit.Admin.Application.Plataforma.Notificaciones;

/// <summary>
/// Vista del buzón de pruebas de notificaciones (HU #11366, Feature #11349, fila única
/// <c>admin.notification_test_settings</c> sembrada por la HU #11365).
/// </summary>
/// <param name="IsConfigured">
/// <c>false</c> cuando <see cref="TestRecipientEmail"/> es <c>null</c> — AC2: la pantalla
/// necesita distinguir "sin configurar" de una cadena vacía.
/// </param>
/// <param name="TestRecipientEmail">Buzón de pruebas, o <c>null</c> si aún no se configuró.</param>
/// <param name="LastTestSentAt">
/// Marca del último envío de prueba. La HU #11366 solo la LEE — la escribe la HU #11368.
/// </param>
/// <param name="RowVersion">Token de concurrencia optimista de la fila única.</param>
public sealed record NotificationTestMailboxView(
    bool IsConfigured,
    string? TestRecipientEmail,
    DateTimeOffset? LastTestSentAt,
    long RowVersion);

/// <summary>Petición de actualización del buzón de pruebas (AC3/AC4).</summary>
/// <param name="Email">Dirección de correo nueva. Se valida el formato antes de persistir.</param>
/// <param name="RowVersion">
/// Token de concurrencia optimista leído previamente por el cliente. <c>null</c> = no verificar
/// concurrencia (compatibilidad con el primer guardado tras la siembra de la migración).
/// </param>
public sealed record UpdateNotificationTestMailboxRequest(string? Email, long? RowVersion);

/// <summary>Desenlace de <see cref="INotificationTestMailboxAdminService.UpdateRecipientAsync"/>.</summary>
public enum NotificationTestMailboxWriteStatus
{
    Ok,
    InvalidEmail,
    Conflict,
}

/// <summary>
/// Lectura y actualización del buzón de pruebas de notificaciones. Alcance EXACTO de la
/// HU #11366: solo el grupo de rutas SuperAdmin y este buzón — nada de canales (HU #11367) ni
/// envío de prueba (HU #11368).
/// </summary>
public interface INotificationTestMailboxAdminService
{
    Task<NotificationTestMailboxView> GetAsync(CancellationToken ct = default);

    Task<(NotificationTestMailboxWriteStatus Status, NotificationTestMailboxView? View)> UpdateRecipientAsync(
        UpdateNotificationTestMailboxRequest request,
        Guid? userId,
        CancellationToken ct = default);
}
