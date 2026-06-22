using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceEventConfiguration : IEntityTypeConfiguration<ProcedureInstanceEvent>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceEvent> builder)
    {
        builder.ToTable("procedure_instance_events", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_events_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Tipo).HasColumnName("tipo").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'{}'");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId, x.CreatedAt })
            .HasDatabaseName("ix_procedure_instance_events_tenant_id_instance_created");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.Events)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_events_procedure_instances");
    }
}
