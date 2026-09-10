using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class UserUiPreferenceEntityConfiguration : IEntityTypeConfiguration<UserUiPreferenceEntity>
{
    public void Configure(EntityTypeBuilder<UserUiPreferenceEntity> builder)
    {
        builder.ToTable("user_ui_preferences", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(60).IsRequired();

        // Unicidad tenant+usuario+scope (uq_user_ui_preferences_tenant_user_scope): un usuario
        // solo puede tener UNA preferencia guardada por scope — el upsert se apoya en ella.
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.Scope })
            .IsUnique()
            .HasDatabaseName("uq_user_ui_preferences_tenant_user_scope");

        builder.HasIndex(x => new { x.TenantId, x.UserId })
            .HasDatabaseName("ix_user_ui_preferences_tenant_user");

        builder.Property(x => x.Value).HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L);
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
