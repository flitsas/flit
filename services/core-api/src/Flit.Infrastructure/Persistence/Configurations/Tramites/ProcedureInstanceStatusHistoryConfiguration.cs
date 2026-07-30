using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceStatusHistoryConfiguration : IEntityTypeConfiguration<ProcedureInstanceStatusHistory>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceStatusHistory> builder)
    {
        builder.ToTable("procedure_instance_status_history", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_status_history_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.FromStatus).HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChangedAt).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.Metadata).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");

        // Feature #11076 (G3): columnas de auditoría enriquecida — nullable, backfill NULL.
        builder.Property(x => x.RoleIdAtTime).HasColumnName("role_id_at_time");
        builder.Property(x => x.OrganizationIdAtTime).HasColumnName("organization_id_at_time");
        builder.Property(x => x.OrganizationTypeAtTime)
            .HasColumnName("organization_type_at_time")
            .HasMaxLength(20);

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_procedure_instance_status_history_tenant_id_instance");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_status_history_procedure_instances");
    }
}
