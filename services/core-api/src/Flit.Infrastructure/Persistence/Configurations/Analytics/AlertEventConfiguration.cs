using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>Mapea <c>analytics.alert_events</c> (Reportes 2.0, HU-D §8 del contrato).</summary>
internal sealed class AlertEventConfiguration : IEntityTypeConfiguration<AlertEvent>
{
    public void Configure(EntityTypeBuilder<AlertEvent> builder)
    {
        builder.ToTable("alert_events", SchemaNames.Analytics);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.AlertRuleId).IsRequired();
        builder.Property(x => x.TriggeredAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.MetricValue).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Threshold).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.Notified).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.Recipients)
            .HasConversion(RecipientsJsonb.Converter, RecipientsJsonb.Comparer)
            .HasColumnType("jsonb").HasDefaultValueSql("'[]'").IsRequired();

        builder.Property(x => x.Message);

        builder.HasOne<AlertRule>()
            .WithMany()
            .HasForeignKey(x => x.AlertRuleId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_alert_events_alert_rules");

        builder.HasIndex(x => new { x.TenantId, x.TriggeredAt })
            .HasDatabaseName("ix_alert_events_tenant_id_triggered_at");
        builder.HasIndex(x => new { x.AlertRuleId, x.TriggeredAt })
            .HasDatabaseName("ix_alert_events_alert_rule_id_triggered_at");
    }
}
