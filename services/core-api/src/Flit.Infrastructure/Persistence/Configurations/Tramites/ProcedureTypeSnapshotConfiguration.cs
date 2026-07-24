using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo de <see cref="ProcedureTypeSnapshot"/> a <c>tramites.procedure_type_snapshots</c>
/// (FEATURE-08 / CFD-01 / AC#5). Inmutable post-INSERT: sin <c>updated_at/by</c> ni
/// <c>deleted_at/by</c>. RLS por <c>tenant_id</c>. Sin <c>row_version</c> (inmutable).
/// DDL gestionado por migración SQL cruda <c>37-F08-procedure-type-snapshots.sql</c>.
/// </summary>
internal sealed class ProcedureTypeSnapshotConfiguration : IEntityTypeConfiguration<ProcedureTypeSnapshot>
{
    public void Configure(EntityTypeBuilder<ProcedureTypeSnapshot> builder)
    {
        builder.ToTable("procedure_type_snapshots", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ProcedureInstanceId).IsRequired();
        builder.HasIndex(x => x.ProcedureInstanceId)
            .IsUnique()
            .HasDatabaseName("uq_procedure_type_snapshots_instance");

        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.TypeVersion).IsRequired();
        builder.Property(x => x.Snapshot)
            .HasColumnType("jsonb")
            .IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_procedure_type_snapshots_tenant_id");
        builder.HasIndex(x => x.ProcedureTypeId)
            .HasDatabaseName("ix_procedure_type_snapshots_procedure_type_id");
    }
}
