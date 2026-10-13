using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class AiProviderTransitionConfiguration
    : IEntityTypeConfiguration<AiProviderTransitionRow>
{
    public void Configure(EntityTypeBuilder<AiProviderTransitionRow> builder)
    {
        builder.ToTable("ai_provider_transitions", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FromProvider).HasColumnName("from_provider").HasMaxLength(32);
        builder.Property(x => x.ToProvider).HasColumnName("to_provider").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Detail).HasColumnName("detail").HasMaxLength(1000);
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()");
    }
}
