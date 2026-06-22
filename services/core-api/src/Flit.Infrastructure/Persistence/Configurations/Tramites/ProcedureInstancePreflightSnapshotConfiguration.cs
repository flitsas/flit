using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstancePreflightSnapshotConfiguration : IEntityTypeConfiguration<ProcedureInstancePreflightSnapshot>
{
    public void Configure(EntityTypeBuilder<ProcedureInstancePreflightSnapshot> builder)
    {
        builder.ToTable("procedure_instance_preflight_snapshots", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_preflight_snapshots_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Overall).HasColumnName("overall").HasMaxLength(10).IsRequired();
        builder.Property(x => x.Checks).HasColumnName("checks").HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(40);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId, x.CreatedAt })
            .HasDatabaseName("ix_procedure_instance_preflight_snapshots_tenant_id_instance_created");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.PreflightSnapshots)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_preflight_snapshots_procedure_instances");
    }
}
