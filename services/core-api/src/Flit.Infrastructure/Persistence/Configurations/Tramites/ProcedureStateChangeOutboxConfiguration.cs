using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureStateChangeOutboxConfiguration
    : IEntityTypeConfiguration<ProcedureStateChangeOutbox>
{
    public void Configure(EntityTypeBuilder<ProcedureStateChangeOutbox> builder)
    {
        builder.ToTable("procedure_state_change_outbox", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.FromStatus).HasColumnName("from_status").HasMaxLength(20);
        builder.Property(x => x.ToStatus).HasColumnName("to_status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500);
        builder.Property(x => x.ChangedByUserId).HasColumnName("changed_by_user_id");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.Attempts).HasColumnName("attempts").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.ProcedureInstanceId)
            .HasDatabaseName("ix_procedure_state_change_outbox_instance");

        // Cola de pendientes (published_at IS NULL) para el worker de despacho.
        builder.HasIndex(x => x.OccurredAt)
            .HasDatabaseName("ix_procedure_state_change_outbox_unpublished")
            .HasFilter("published_at IS NULL");
    }
}
