using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo de <c>admin.mandate_format_settings</c> (HU #13169). El DDL (124) crea tabla, CHECK, índice único por nombre y
/// trigger de row_version; la entidad se excluye de migraciones (mismo patrón que la config de mandato por OT).
/// </summary>
internal sealed class MandateFormatSettingConfiguration : IEntityTypeConfiguration<MandateFormatSettingEntity>
{
    public void Configure(EntityTypeBuilder<MandateFormatSettingEntity> builder)
    {
        builder.ToTable("mandate_format_settings", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_mandate_format_settings_row_version");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.FormatCode).HasColumnName("format_code").HasMaxLength(30).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.AssignmentMode).HasColumnName("assignment_mode").HasMaxLength(20).IsRequired();
        builder.Property(x => x.CurrentVersion).HasColumnName("current_version").HasDefaultValue(0);
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(x => x.FormatCode).IsUnique().HasDatabaseName("uq_mandate_format_settings_code");
    }
}

/// <summary>Mapeo de <c>admin.mandate_format_versions</c> (HU #13169): versiones inmutables (trigger en el DDL 124).</summary>
internal sealed class MandateFormatVersionConfiguration : IEntityTypeConfiguration<MandateFormatVersionEntity>
{
    public void Configure(EntityTypeBuilder<MandateFormatVersionEntity> builder)
    {
        builder.ToTable("mandate_format_versions", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_mandate_format_versions_immutable");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.FormatSettingId).HasColumnName("format_setting_id").IsRequired();
        builder.Property(x => x.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").IsRequired();
        builder.Property(x => x.BodySha256).HasColumnName("body_sha256").HasMaxLength(64).IsFixedLength().IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");

        builder.HasIndex(x => new { x.FormatSettingId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("uq_mandate_format_versions_number");
        builder.HasOne<MandateFormatSettingEntity>()
            .WithMany()
            .HasForeignKey(x => x.FormatSettingId)
            .HasConstraintName("fk_mandate_format_versions_setting")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
