using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>Mapea <c>analytics.report_sla_config</c> (Feature #11076).</summary>
internal sealed class ReportSlaConfigConfiguration : IEntityTypeConfiguration<ReportSlaConfig>
{
    public void Configure(EntityTypeBuilder<ReportSlaConfig> builder)
    {
        builder.ToTable("report_sla_config", SchemaNames.Analytics, t =>
        {
            t.HasTrigger("tr_report_sla_config_row_version");
            t.HasTrigger("tr_report_sla_config_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId);
        builder.Property(x => x.ProcedureType).HasMaxLength(50);

        builder.Property(x => x.SlaHours).IsRequired();
        builder.Property(x => x.CalendarType).HasMaxLength(20).IsRequired().HasDefaultValue("business");
        builder.Property(x => x.EffectiveFrom).IsRequired().HasDefaultValueSql("CURRENT_DATE");
        builder.Property(x => x.EffectiveTo);

        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy);

        // Índice para lookup activo — partial no disponible en fluent API de EF,
        // se gestiona directamente en el DDL (45-F11076-reporting-v2.sql).
        builder.HasIndex(x => new { x.TenantId, x.ProcedureType, x.TransitOfficeId })
            .HasDatabaseName("ix_report_sla_config_tenant_lookup");

        builder.HasIndex(x => x.TransitOfficeId)
            .HasDatabaseName("ix_report_sla_config_transit_office_id");
    }
}
