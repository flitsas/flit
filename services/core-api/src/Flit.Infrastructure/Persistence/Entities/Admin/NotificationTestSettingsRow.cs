namespace Flit.Infrastructure.Persistence.Entities.Admin;

/// <summary>
/// Fila de <c>admin.notification_test_settings</c> — configuración del banco de pruebas de
/// notificaciones (HU #11365, Feature #11349). Es una fila única y global de plataforma: no lleva
/// <c>TenantId</c> porque el buzón de pruebas lo gestiona el SuperAdmin y no pertenece a ningún
/// cliente.
/// </summary>
/// <remarks>
/// Entidad de persistencia, no modelo de dominio. La leen y escriben los endpoints de la HU #11366;
/// el sello de <see cref="LastTestSentAt"/> lo pone la HU #11368 (envío de prueba y límite de
/// frecuencia). Aquí solo existe la forma.
/// </remarks>
internal sealed class NotificationTestSettingsRow
{
    public Guid Id { get; set; }

    /// <summary>
    /// Buzón de pruebas. <c>null</c> = todavía no configurado (la fila sembrada por la migración
    /// nace así). Dato personal: no reutilizar fuera del banco de pruebas.
    /// </summary>
    public string? TestRecipientEmail { get; set; }

    /// <summary>
    /// Marca del último envío de prueba. <c>null</c> = nunca se envió uno. Persistida para que el
    /// límite de frecuencia sobreviva a un reinicio y valga entre réplicas.
    /// </summary>
    public DateTimeOffset? LastTestSentAt { get; set; }

    /// <summary>Token de concurrencia optimista; lo incrementa el trigger de la tabla.</summary>
    public long RowVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }
}
