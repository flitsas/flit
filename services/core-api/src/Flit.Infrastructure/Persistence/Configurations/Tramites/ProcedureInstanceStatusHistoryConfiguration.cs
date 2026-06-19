using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceStatusHistoryConfiguration : IEntityTypeConfiguration<ProcedureInstanceStatusHistory>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceStatusHistory> builder)
    {
        builder.ToTable("procedure_instance_status_history", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.FromStatus).HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ChangedAt).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.Metadata).HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_procedure_instance_status_history_tenant_id_instance");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.StatusHistory)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_status_history_procedure_instances");
    }
}
