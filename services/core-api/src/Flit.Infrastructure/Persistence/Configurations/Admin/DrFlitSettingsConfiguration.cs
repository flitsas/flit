using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>Mapea <c>admin.dr_flit_settings</c> (HU #14771). DDL por SQL crudo; excluida de migraciones EF.</summary>
internal sealed class DrFlitSettingsConfiguration : IEntityTypeConfiguration<DrFlitSettingsRow>
{
    public void Configure(EntityTypeBuilder<DrFlitSettingsRow> builder)
    {
        builder.ToTable("dr_flit_settings", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.UiEnabled).HasColumnName("ui_enabled");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
    }
}
