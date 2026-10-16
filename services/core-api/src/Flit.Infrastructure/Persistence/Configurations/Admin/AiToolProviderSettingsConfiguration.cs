using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class AiToolProviderSettingsConfiguration
    : IEntityTypeConfiguration<AiToolProviderSettingsRow>
{
    public void Configure(EntityTypeBuilder<AiToolProviderSettingsRow> builder)
    {
        builder.ToTable("ai_tool_provider_settings", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ToolCode).HasColumnName("tool_code").HasMaxLength(32).IsRequired();
        builder.HasIndex(x => x.ToolCode).IsUnique();

        builder.Property(x => x.ActiveProvider).HasColumnName("active_provider").HasMaxLength(32).IsRequired();
        builder.Property(x => x.AutoReturnToPrimary).HasColumnName("auto_return_to_primary").IsRequired();
        builder.Property(x => x.ProvidersJson).HasColumnName("providers_json").HasColumnType("jsonb").IsRequired();

        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
    }
}
