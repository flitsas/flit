using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="ProcedureTypeSource"/> a <c>tramites.procedure_type_sources</c>
/// (FEATURE-08 / CFD-04). Catálogo global sin <c>tenant_id</c>: excepción ADR-0019;
/// sin <c>HasQueryFilter</c> de tenant. PK compuesta; DDL gestionado por migración
/// SQL cruda <c>36-F08-procedure-type-sources.sql</c>.
/// </summary>
internal sealed class ProcedureTypeSourceConfiguration : IEntityTypeConfiguration<ProcedureTypeSource>
{
    public void Configure(EntityTypeBuilder<ProcedureTypeSource> builder)
    {
        builder.ToTable("procedure_type_sources", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => new { x.ProcedureTypeId, x.ExternalDataSourceId });

        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.ExternalDataSourceId).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.ExecutionOrder).IsRequired().HasDefaultValue((short)0);
        builder.Property(x => x.Config)
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'{}'");
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.ExternalDataSourceId)
            .HasDatabaseName("ix_procedure_type_sources_source_id");
    }
}
