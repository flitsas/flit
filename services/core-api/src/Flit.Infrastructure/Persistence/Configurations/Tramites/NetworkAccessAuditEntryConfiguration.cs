using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #12361 — auditoría append-only del acceso consolidado (<c>tramites.network_access_audit</c>).
/// Sin FK a <c>identity.tenants</c> ni a <c>tramites.procedure_instances</c> a propósito (la fila
/// sobrevive al desvínculo); sin soft delete. Los CHECK de <c>result</c>/<c>resource</c>, el índice
/// GIN sobre <c>reached_tenant_ids</c>, la policy RLS y el trigger de inmutabilidad viven solo en SQL
/// (113-HU12361-network-access-audit.sql).
/// </summary>
internal sealed class NetworkAccessAuditEntryConfiguration : IEntityTypeConfiguration<NetworkAccessAuditEntry>
{
    public void Configure(EntityTypeBuilder<NetworkAccessAuditEntry> builder)
    {
        builder.ToTable("network_access_audit", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_network_access_audit_immutable"));

        builder.HasKey(x => x.Id).HasName("pk_network_access_audit");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.ActorTenantId).HasColumnName("actor_tenant_id").IsRequired();
        builder.Property(x => x.ReachedTenantIds).HasColumnName("reached_tenant_ids").HasColumnType("uuid[]").IsRequired();
        builder.Property(x => x.Resource).HasColumnName("resource").HasColumnType("text").IsRequired();
        builder.Property(x => x.Filters).HasColumnName("filters").HasColumnType("jsonb");
        builder.Property(x => x.ProcedureId).HasColumnName("procedure_id");
        builder.Property(x => x.ProcedureTenantId).HasColumnName("procedure_tenant_id");
        builder.Property(x => x.AttachmentId).HasColumnName("attachment_id");
        builder.Property(x => x.Result).HasColumnName("result").HasColumnType("text").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasIndex(x => new { x.ProcedureTenantId, x.OccurredAt })
            .HasDatabaseName("ix_network_access_audit_procedure_tenant_occurred_at")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.ActorTenantId, x.OccurredAt })
            .HasDatabaseName("ix_network_access_audit_actor_tenant_occurred_at")
            .IsDescending(false, true);

        builder.HasIndex(x => x.ReachedTenantIds)
            .HasDatabaseName("ix_network_access_audit_reached_tenant_ids")
            .HasMethod("gin");
    }
}
