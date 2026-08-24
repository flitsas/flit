using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureTypeConfiguration : IEntityTypeConfiguration<ProcedureType>
{
    public void Configure(EntityTypeBuilder<ProcedureType> builder)
    {
        // DDL gestionado por migración SQL cruda (HU #10151); la entidad vive en el modelo
        // EF solo para consultas y se excluye del snapshot/migraciones EF.
        builder.ToTable("procedure_types", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_procedure_types_code");

        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Family).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.Property(x => x.PublicationStatus).HasMaxLength(20).IsRequired().HasDefaultValue("draft");
        builder.Property(x => x.ExternalRefs).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");

        // F08 Fase 2b: versión semántica + perfil de conformación (CFD-01)
        builder.Property(x => x.Version).IsRequired().HasDefaultValue(1);
        builder.Property(x => x.GateProfile).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");

        // ADR-0050 — barrera de operación. La columna la crea el DDL 79; aquí solo se mapea, como el
        // resto de esta tabla (ExcludeFromMigrations).
        builder.Property(x => x.WizardEnabled)
            .HasColumnName("wizard_enabled")
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasMany(x => x.ConformationRules)
            .WithOne(x => x.ProcedureType)
            .HasForeignKey(x => x.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Steps)
            .WithOne(x => x.ProcedureType)
            .HasForeignKey(x => x.ProcedureTypeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
