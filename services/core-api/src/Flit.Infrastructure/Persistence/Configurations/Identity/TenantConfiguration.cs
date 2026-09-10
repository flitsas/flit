using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Identity;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", SchemaNames.Identity);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_tenants_code");

        builder.Property(x => x.LegalName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TaxId).HasMaxLength(20).IsRequired();
        builder.Property(x => x.TenantType).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);

        // HU #12318 — jerarquía padre-hija (profundidad 2). CHECK anti-autorreferencia y trigger
        // bidireccional viven solo en SQL (107-HU12318-tenant-parent-hierarchy.sql).
        builder.Property(x => x.ParentTenantId).HasColumnName("parent_tenant_id");
        // HU #12406 — la clase de la cabeza es un valor de tenant_type (CONCESION | MARCA_BLANCA) y
        // is_group_parent queda acoplado a él por ck_tenants_group_parent_by_type; ese CHECK, el
        // catálogo ampliado y la rama de inmutabilidad del trigger viven solo en SQL
        // (109-HU12406-head-tenant-types-and-parent-snapshot.sql).
        builder.Property(x => x.IsGroupParent).HasColumnName("is_group_parent").HasDefaultValue(false);
        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(x => x.ParentTenantId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_tenants_parent_tenant");
        builder.HasIndex(x => x.ParentTenantId)
            .HasDatabaseName("ix_tenants_parent_tenant_id")
            .HasFilter("parent_tenant_id IS NOT NULL");

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
    }
}
