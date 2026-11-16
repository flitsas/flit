using Flit.Infrastructure.Persistence.Entities.Identity;
using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Security;

/// <summary><c>security.product_role_suspensions</c> (ADR-0089, HU #14587). Ver <see cref="ProductRoleSuspensionEntity"/>.</summary>
internal sealed class ProductRoleSuspensionConfiguration : IEntityTypeConfiguration<ProductRoleSuspensionEntity>
{
    public void Configure(EntityTypeBuilder<ProductRoleSuspensionEntity> builder)
    {
        builder.ToTable("product_role_suspensions", SchemaNames.Security, t =>
        {
            t.HasComment("Suspensión de un usuario en un producto (ADR-0089): la asignación del rol del producto se cierra y esta fila permite listarlo como SUSPENDIDO y reactivarlo con el mismo rol. No suspende la cuenta.");
            t.HasCheckConstraint("ck_product_role_suspensions_product_code", "product_code <> ''");
        });
        builder.HasKey(x => x.Id).HasName("pk_product_role_suspensions");
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.ProductCode).HasMaxLength(32).IsRequired();
        builder.Property(x => x.RoleId).HasComment("Rol del producto que tenía al suspenderse; se recrea al reactivar.");
        builder.Property(x => x.SuspendedAt).HasDefaultValueSql("now()");
        builder.Property(x => x.SuspendedBy).HasComment("Usuario que suspendió (@pii:low).");

        builder.HasIndex(x => new { x.TenantId, x.UserId, x.ProductCode })
            .IsUnique()
            .HasDatabaseName("uq_product_role_suspensions");
        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_product_role_suspensions_user_id");
        builder.HasIndex(x => x.RoleId).HasDatabaseName("ix_product_role_suspensions_role_id");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_product_role_suspensions_tenants").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_product_role_suspensions_users").OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Role>().WithMany().HasForeignKey(x => x.RoleId)
            .HasConstraintName("fk_product_role_suspensions_roles").OnDelete(DeleteBehavior.Restrict);
    }
}
