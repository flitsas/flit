using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstancePrendaConfiguration : IEntityTypeConfiguration<ProcedureInstancePrenda>
{
    public void Configure(EntityTypeBuilder<ProcedureInstancePrenda> builder)
    {
        // DDL gestionado por migración SQL cruda (24-HU10585-prenda.sql): índice único parcial
        // (una vigente por instancia), CHECKs, RLS y triggers que el MigrationBuilder no modela.
        // La entidad se excluye de migraciones (mismo patrón que ProcedureInstance); se declaran los
        // triggers para que EF emita UPDATE compatible con row_version como concurrency token.
        builder.ToTable("procedure_instance_prenda", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_procedure_instance_prenda_row_version");
            t.HasTrigger("tr_procedure_instance_prenda_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Decision).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Estado).HasMaxLength(20).IsRequired().HasDefaultValue("vigente");
        builder.Property(x => x.AcreedorNombre).HasColumnName("acreedor_nombre").HasMaxLength(200);
        builder.Property(x => x.AcreedorDocumento).HasColumnName("acreedor_documento").HasMaxLength(20);
        builder.Property(x => x.LevantamientoEntidad)
            .HasColumnName("levantamiento_entidad").HasMaxLength(200);
        builder.Property(x => x.Metadata).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.ProcedureInstanceId)
            .HasDatabaseName("ix_procedure_instance_prenda_instance");
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_procedure_instance_prenda_tenant_id");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany()
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_prenda_procedure_instances");
    }
}
