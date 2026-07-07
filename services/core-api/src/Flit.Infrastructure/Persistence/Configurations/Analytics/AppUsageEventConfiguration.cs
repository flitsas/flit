using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>Config EF de <c>analytics.app_usage_events</c> (HU-A Reportes 2.0, contrato §7).</summary>
internal sealed class AppUsageEventConfiguration : IEntityTypeConfiguration<AppUsageEvent>
{
    public void Configure(EntityTypeBuilder<AppUsageEvent> builder)
    {
        builder.ToTable("app_usage_events", SchemaNames.Analytics);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Module).HasColumnName("module").HasMaxLength(40);
        builder.Property(x => x.StepKey).HasColumnName("step_key").HasMaxLength(40);
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id");
        builder.Property(x => x.DurationMs).HasColumnName("duration_ms");
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb")
            .IsRequired().HasDefaultValueSql("'{}'::jsonb");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired().HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.TenantId, x.OccurredAt })
            .HasDatabaseName("ix_app_usage_events_tenant_occurred");
        builder.HasIndex(x => new { x.TenantId, x.EventType, x.OccurredAt })
            .HasDatabaseName("ix_app_usage_events_tenant_type_occurred");
        builder.HasIndex(x => x.ProcedureInstanceId)
            .HasDatabaseName("ix_app_usage_events_instance")
            .HasFilter("procedure_instance_id IS NOT NULL");
    }
}
