using Flit.Infrastructure.Persistence.Entities.Security;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Security;

internal sealed class UserTempSuspensionConfiguration : IEntityTypeConfiguration<UserTempSuspension>
{
    public void Configure(EntityTypeBuilder<UserTempSuspension> builder)
    {
        builder.ToTable("user_temp_suspensions", SchemaNames.Security);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.StartsAt).IsRequired();
        builder.Property(x => x.EndsAt).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(500).IsRequired();

        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.UserId })
            .HasDatabaseName("ix_user_temp_suspensions_tenant_id_user_id");

        builder.HasOne(x => x.Tenant)
            .WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_user_temp_suspensions_tenants")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .HasConstraintName("fk_user_temp_suspensions_users")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
