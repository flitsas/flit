using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del puente representante ↔ tipo de trámite (HU #10900, ADR-0033). El DDL lo gestiona
/// la migración SQL cruda: índice único (representante, tipo), FK en cascada al representante, FK al
/// catálogo de tipos y RLS transitiva. Entidad ExcludeFromMigrations.
/// </summary>
internal sealed class CompanyLegalRepresentativeProcedureTypeConfiguration
    : IEntityTypeConfiguration<CompanyLegalRepresentativeProcedureTypeEntity>
{
    public void Configure(EntityTypeBuilder<CompanyLegalRepresentativeProcedureTypeEntity> builder)
    {
        builder.ToTable("company_legal_representative_procedure_types", SchemaNames.Admin, t =>
            t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.RepresentativeId).IsRequired();
        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.RepresentativeId, x.ProcedureTypeId })
            .IsUnique()
            .HasDatabaseName("uq_clr_procedure_types_representative_type");
        builder.HasIndex(x => x.ProcedureTypeId)
            .HasDatabaseName("ix_clr_procedure_types_procedure_type_id");

        builder.HasOne<CompanyLegalRepresentativeEntity>()
            .WithMany()
            .HasForeignKey(x => x.RepresentativeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_clr_procedure_types_representative");

        builder.HasOne<ProcedureType>()
            .WithMany()
            .HasForeignKey(x => x.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_clr_procedure_types_procedure_type");
    }
}
