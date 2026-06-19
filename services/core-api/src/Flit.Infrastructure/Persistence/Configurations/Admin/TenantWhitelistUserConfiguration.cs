using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantWhitelistUserConfiguration
    : IEntityTypeConfiguration<TenantWhitelistUser>
{
    public void Configure(EntityTypeBuilder<TenantWhitelistUser> builder)
    {
        builder.ToTable("tenant_whitelist_users", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(320).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500);

        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Email })
            .IsUnique()
            .HasDatabaseName("uq_tenant_whitelist_users_tenant_email");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_tenant_whitelist_users_tenant_id");
    }
}
