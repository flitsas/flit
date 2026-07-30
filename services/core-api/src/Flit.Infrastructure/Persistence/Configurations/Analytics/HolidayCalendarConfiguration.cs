using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>
/// Mapea <c>analytics.holiday_calendar</c> (Feature #11076).
/// Catálogo mixto: tenant_id IS NULL = global; IS NOT NULL = per-tenant.
/// La unicidad real usa UNIQUE NULLS NOT DISTINCT (PG 17+) en el DDL SQL.
/// EF registra el índice convencional; la constraint de unicidad se gestiona en el DDL.
/// </summary>
internal sealed class HolidayCalendarConfiguration : IEntityTypeConfiguration<HolidayCalendar>
{
    public void Configure(EntityTypeBuilder<HolidayCalendar> builder)
    {
        builder.ToTable("holiday_calendar", SchemaNames.Analytics);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        // TenantId nullable: NULL = global, NOT NULL = tenant-specific (A4 flexible para catálogos mixtos)
        builder.Property(x => x.TenantId).HasColumnName("tenant_id");

        builder.Property(x => x.HolidayDate).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(5).IsRequired().HasDefaultValue("CO");
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

        builder.Property(x => x.ExternalRefs)
            .HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt);

        // Índice principal de lookup por tenant (global=NULL o tenant específico), país y fecha.
        // El constraint UNIQUE NULLS NOT DISTINCT (uq_holiday_calendar_tenant_date_country)
        // vive en el DDL SQL; EF solo registra el índice de acceso sin unicidad duplicada.
        builder.HasIndex(x => new { x.TenantId, x.CountryCode, x.HolidayDate })
            .HasDatabaseName("ix_holiday_calendar_tenant_country_date")
            .HasFilter("is_active = true");
    }
}
