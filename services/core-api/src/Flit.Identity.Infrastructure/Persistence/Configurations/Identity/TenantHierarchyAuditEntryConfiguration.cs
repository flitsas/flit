using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Identity;

/// <summary>
/// HU #12323 — bitácora append-only del vínculo padre-hija. Sin FK a <c>identity.tenants</c> a
/// propósito (la fila sobrevive a desvínculos y borrados); sin <c>tenant_id</c>, RLS ni soft delete.
/// El CHECK de <c>action</c> y el trigger de inmutabilidad viven solo en SQL
/// (108-HU12323-hierarchy-switches-and-link-audit.sql).
/// </summary>
internal sealed class TenantHierarchyAuditEntryConfiguration : IEntityTypeConfiguration<TenantHierarchyAuditEntry>
{
    public void Configure(EntityTypeBuilder<TenantHierarchyAuditEntry> builder)
    {
        builder.ToTable("tenant_hierarchy_audit", SchemaNames.Identity, t =>
            t.HasTrigger("tr_tenant_hierarchy_audit_immutable"));

        builder.HasKey(x => x.Id).HasName("pk_tenant_hierarchy_audit");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ParentTenantId).HasColumnName("parent_tenant_id").IsRequired();
        builder.Property(x => x.ChildTenantId).HasColumnName("child_tenant_id").IsRequired();
        builder.Property(x => x.Action).HasColumnName("action").HasColumnType("text").IsRequired();
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => new { x.ParentTenantId, x.OccurredAt })
            .HasDatabaseName("ix_tenant_hierarchy_audit_parent_occurred_at")
            .IsDescending(false, true);

        builder.HasIndex(x => new { x.ChildTenantId, x.OccurredAt })
            .HasDatabaseName("ix_tenant_hierarchy_audit_child_occurred_at")
            .IsDescending(false, true);
    }
}
