using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>Mapea <c>analytics.report_schedules</c> (Reportes 2.0, HU-D §8 del contrato).</summary>
internal sealed class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        builder.ToTable("report_schedules", SchemaNames.Analytics);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId);
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.ReportType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.SavedQueryId);
        builder.Property(x => x.SavedQueryScope).HasMaxLength(10);
        builder.Property(x => x.Frequency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DayOfWeek);
        builder.Property(x => x.DayOfMonth);
        builder.Property(x => x.SendHour).IsRequired().HasDefaultValue((short)7);
        builder.Property(x => x.Format).HasMaxLength(10).IsRequired();

        builder.Property(x => x.Recipients)
            .HasConversion(RecipientsJsonb.Converter, RecipientsJsonb.Comparer)
            .HasColumnType("jsonb").HasDefaultValueSql("'[]'").IsRequired();

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.LastSentAt);
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.DeletedAt);

        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_report_schedules_tenant_id");
    }
}
