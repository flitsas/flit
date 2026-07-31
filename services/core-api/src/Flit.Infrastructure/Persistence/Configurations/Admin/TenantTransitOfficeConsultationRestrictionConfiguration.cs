using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantTransitOfficeConsultationRestrictionConfiguration
    : IEntityTypeConfiguration<TenantTransitOfficeConsultationRestriction>
{
    public void Configure(EntityTypeBuilder<TenantTransitOfficeConsultationRestriction> builder)
    {
        builder.ToTable("tenant_transit_office_consultation_restrictions", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();

        builder.Property(x => x.ConsultationKind)
            .IsRequired()
            .HasColumnType("text");

        builder.Property(x => x.Enabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Hace atómico el upsert (SetAsync busca por esta tripleta antes de decidir add/update).
        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId, x.ConsultationKind })
            .IsUnique()
            .HasDatabaseName("uq_tenant_transit_office_consultation_restrictions");

        // Índice general por tenant (tenant_id primero, checklist §A11).
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_tenant_transit_office_consultation_restrictions_tenant_id");

        // Índice PARCIAL: cubre la query caliente del preflight (kinds inhabilitados por
        // tenant+OT). El filtro SQL se declara en el DDL embebido (33-F05-...sql); aquí solo
        // se anota para que el snapshot de EF no intente recrearlo sin el WHERE.
        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId })
            .HasDatabaseName("ix_tenant_transit_office_consultation_restrictions_disabled")
            .HasFilter("enabled = false");
    }
}
