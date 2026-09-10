using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF de las causales marcadas en un rechazo. Esquema en DDL crudo
/// (<c>56-causales-rechazo.sql</c>), por eso <c>ExcludeFromMigrations</c>.
/// </summary>
internal sealed class ProcedureInstanceRejectionReasonConfiguration
    : IEntityTypeConfiguration<ProcedureInstanceRejectionReason>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceRejectionReason> builder)
    {
        builder.ToTable(
            "procedure_instance_rejection_reasons",
            SchemaNames.Tramites,
            t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.StatusHistoryId).HasColumnName("status_history_id");
        builder.Property(x => x.RejectionReasonId).HasColumnName("rejection_reason_id").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(x => x.ProcedureInstanceId).HasDatabaseName("ix_pirr_instance");
        builder.HasIndex(x => new { x.TenantId, x.RejectionReasonId })
            .HasDatabaseName("ix_pirr_tenant_reason");

        // La relación con el evento de rechazo se declara para que EF ORDENE los inserts: el
        // rechazo crea la fila de historial y sus causales en el mismo SaveChanges, y sin esta
        // relación EF puede insertar las causales primero y violar la FK en PostgreSQL. No genera
        // DDL (la tabla está ExcludeFromMigrations); el esquema lo lleva 56-causales-rechazo.sql.
        builder.HasOne<ProcedureInstanceStatusHistory>()
            .WithMany()
            .HasForeignKey(x => x.StatusHistoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ProcedureInstance>()
            .WithMany()
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
