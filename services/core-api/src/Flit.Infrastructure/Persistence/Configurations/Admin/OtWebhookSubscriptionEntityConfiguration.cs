using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class OtWebhookSubscriptionEntityConfiguration : IEntityTypeConfiguration<OtWebhookSubscriptionEntity>
{
    public void Configure(EntityTypeBuilder<OtWebhookSubscriptionEntity> builder)
    {
        builder.ToTable("ot_webhook_subscriptions", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_ot_webhook_subscriptions_tenant_id");

        builder.Property(x => x.EventType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TargetUrl).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SecretHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.IsActive).HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
