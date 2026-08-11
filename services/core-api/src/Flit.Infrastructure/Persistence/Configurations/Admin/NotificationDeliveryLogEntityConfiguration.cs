using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapea <c>admin.notification_delivery_logs</c> (HU #11363, esquema de la HU #11357).
/// </summary>
internal sealed class NotificationDeliveryLogEntityConfiguration
    : IEntityTypeConfiguration<NotificationDeliveryLogEntity>
{
    public void Configure(EntityTypeBuilder<NotificationDeliveryLogEntity> builder)
    {
        // DDL gestionado por migración SQL cruda (64-notification-delivery-log.sql): el CHECK de
        // result, la FK a identity.tenants y la política RLS no los sabe generar el scaffolding de
        // EF. La entidad vive en el modelo solo para leer/escribir filas, excluida del snapshot.
        builder.ToTable("notification_delivery_logs", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

        builder.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(30).IsRequired();
        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(320).IsRequired();
        builder.Property(x => x.Result).HasColumnName("result").HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(1000);
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms").IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");

        // Misma lectura canónica del DDL: tenant primero, fecha descendente.
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt })
            .HasDatabaseName("ix_notification_delivery_logs_tenant_occurred_at");
    }
}
