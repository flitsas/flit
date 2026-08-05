using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Catalogs;

/// <summary>
/// Mapeo EF del catálogo de causales de rechazo. El esquema lo lleva el DDL crudo
/// (<c>56-causales-rechazo.sql</c>), así que va <c>ExcludeFromMigrations</c>.
/// </summary>
internal sealed class RejectionReasonConfiguration : IEntityTypeConfiguration<RejectionReason>
{
    public void Configure(EntityTypeBuilder<RejectionReason> builder)
    {
        builder.ToTable("rejection_reasons", SchemaNames.Catalogs, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(60).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(150).IsRequired();
        builder.Property(x => x.Modalidad).HasColumnName("modalidad").HasMaxLength(40).IsRequired();
        builder.Property(x => x.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName("uq_rejection_reasons_code");
    }
}
