using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class OtFeatureFlagEntityConfiguration : IEntityTypeConfiguration<OtFeatureFlagEntity>
{
    public void Configure(EntityTypeBuilder<OtFeatureFlagEntity> builder)
    {
        builder.ToTable("ot_feature_flags", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.FlagKey).HasMaxLength(80).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.FlagKey })
            .IsUnique()
            .HasDatabaseName("uq_ot_feature_flags_tenant_key");

        builder.Property(x => x.IsEnabled).HasDefaultValue(false);
        builder.Property(x => x.Config).HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
