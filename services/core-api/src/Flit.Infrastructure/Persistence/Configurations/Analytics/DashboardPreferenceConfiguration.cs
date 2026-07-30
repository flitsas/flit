using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>Mapea <c>analytics.dashboard_preferences</c> (Feature #11076).</summary>
internal sealed class DashboardPreferenceConfiguration : IEntityTypeConfiguration<DashboardPreference>
{
    public void Configure(EntityTypeBuilder<DashboardPreference> builder)
    {
        builder.ToTable("dashboard_preferences", SchemaNames.Analytics, t =>
        {
            t.HasTrigger("tr_dashboard_preferences_row_version");
            t.HasTrigger("tr_dashboard_preferences_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();

        builder.Property(x => x.ConfigJson)
            .HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();

        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy);

        // Unicidad: una fila por (tenant_id, user_id) — upsert semántico
        builder.HasIndex(x => new { x.TenantId, x.UserId })
            .IsUnique()
            .HasDatabaseName("uq_dashboard_preferences_user");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_dashboard_preferences_user_id");
    }
}
