using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class PlateRangeEntityConfiguration : IEntityTypeConfiguration<PlateRangeEntity>
{
    public void Configure(EntityTypeBuilder<PlateRangeEntity> builder)
    {
        builder.ToTable("plate_ranges", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.Prefix).HasMaxLength(3).IsRequired();
        builder.Property(x => x.RangeFrom).IsRequired();
        builder.Property(x => x.RangeTo).IsRequired();
        builder.Property(x => x.EditableUntil).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.TransitOfficeId })
            .HasDatabaseName("ix_plate_ranges_tenant_office");
    }
}
