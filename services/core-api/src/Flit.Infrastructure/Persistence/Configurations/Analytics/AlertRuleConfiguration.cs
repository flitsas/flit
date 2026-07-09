using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>Mapea <c>analytics.alert_rules</c> (Reportes 2.0, HU-D §8 del contrato).</summary>
internal sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> builder)
    {
        builder.ToTable("alert_rules", SchemaNames.Analytics);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Metric).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Operator).HasMaxLength(4).IsRequired();
        builder.Property(x => x.Threshold).HasColumnType("numeric(12,2)").IsRequired();
        builder.Property(x => x.WindowMinutes).IsRequired().HasDefaultValue(1440);
        builder.Property(x => x.CooldownMinutes).IsRequired().HasDefaultValue(240);

        builder.Property(x => x.Recipients)
            .HasConversion(RecipientsJsonb.Converter, RecipientsJsonb.Comparer)
            .HasColumnType("jsonb").HasDefaultValueSql("'[]'").IsRequired();

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.LastTriggeredAt);
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_alert_rules_tenant_id");
    }
}
