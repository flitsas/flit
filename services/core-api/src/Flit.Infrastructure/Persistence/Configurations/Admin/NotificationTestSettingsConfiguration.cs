using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapea <c>admin.notification_test_settings</c> (HU #11365) — fila única del banco de pruebas de
/// notificaciones.
/// </summary>
internal sealed class NotificationTestSettingsConfiguration
    : IEntityTypeConfiguration<NotificationTestSettingsRow>
{
    public void Configure(EntityTypeBuilder<NotificationTestSettingsRow> builder)
    {
        // DDL gestionado por migración SQL cruda (67-HU11365-notification-test-settings.sql): el
        // índice único sobre expresión constante, el trigger de row_version y los comentarios PII no
        // los sabe generar el scaffolding de EF. La entidad vive en el modelo solo para leer/escribir
        // la fila, y se excluye del snapshot y de las migraciones automáticas.
        builder.ToTable("notification_test_settings", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TestRecipientEmail)
            .HasColumnName("test_recipient_email")
            .HasMaxLength(320);

        builder.Property(x => x.LastTestSentAt).HasColumnName("last_test_sent_at");

        // Token de concurrencia: dos SuperAdmin editando el buzón a la vez, o el sello del envío de
        // prueba compitiendo con una edición, no pueden pisarse en silencio. El valor lo incrementa
        // el trigger tr_notification_test_settings_row_version, no la aplicación.
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
    }
}
