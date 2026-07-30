using Flit.Infrastructure.Persistence.Entities.Analytics;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Analytics;

/// <summary>Mapea <c>analytics.saved_queries</c> (Feature #11076).</summary>
internal sealed class SavedQueryConfiguration : IEntityTypeConfiguration<SavedQuery>
{
    public void Configure(EntityTypeBuilder<SavedQuery> builder)
    {
        builder.ToTable("saved_queries", SchemaNames.Analytics, t =>
        {
            t.HasTrigger("tr_saved_queries_row_version");
            t.HasTrigger("tr_saved_queries_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(150).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(500);

        builder.Property(x => x.FiltersJson)
            .HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();

        builder.Property(x => x.IsShared).IsRequired().HasDefaultValue(false);

        builder.Property(x => x.RowVersion).IsConcurrencyToken().HasDefaultValue(0L);

        builder.Property(x => x.CreatedAt).IsRequired().HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.UpdatedAt);
        builder.Property(x => x.UpdatedBy);
        builder.Property(x => x.DeletedAt);
        builder.Property(x => x.DeletedBy);

        builder.HasIndex(x => new { x.TenantId, x.UserId })
            .HasDatabaseName("ix_saved_queries_tenant_user")
            .HasFilter("deleted_at IS NULL");

        builder.HasIndex(x => x.UserId)
            .HasDatabaseName("ix_saved_queries_user_id");
    }
}
