using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class OtApiCallLogEntityConfiguration : IEntityTypeConfiguration<OtApiCallLogEntity>
{
    public void Configure(EntityTypeBuilder<OtApiCallLogEntity> builder)
    {
        builder.ToTable("ot_api_call_logs", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.CalledAt })
            .HasDatabaseName("ix_ot_api_call_logs_tenant_id_called_at");

        builder.Property(x => x.Direction).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Endpoint).HasMaxLength(500).IsRequired();
        builder.Property(x => x.HttpMethod).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.CalledAt).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(500);
    }
}
