using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class AiFailoverAlertConfiguration
    : IEntityTypeConfiguration<AiFailoverAlertRow>
{
    public void Configure(EntityTypeBuilder<AiFailoverAlertRow> builder)
    {
        builder.ToTable("ai_failover_alerts", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TransitionId).HasColumnName("transition_id").IsRequired();
        builder.HasIndex(x => x.TransitionId).IsUnique();

        builder.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FromProvider).HasColumnName("from_provider").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ToProvider).HasColumnName("to_provider").HasMaxLength(32).IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()");
        builder.Property(x => x.AckedAt).HasColumnName("acked_at");
        builder.Property(x => x.AckedBy).HasColumnName("acked_by");
    }
}
