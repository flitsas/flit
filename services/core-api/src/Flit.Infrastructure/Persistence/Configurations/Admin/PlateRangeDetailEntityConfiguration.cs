using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class PlateRangeDetailEntityConfiguration : IEntityTypeConfiguration<PlateRangeDetailEntity>
{
    public void Configure(EntityTypeBuilder<PlateRangeDetailEntity> builder)
    {
        builder.ToTable("plate_range_details", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.PlateRangeId).IsRequired();
        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.Plate).HasMaxLength(10).IsRequired();
        builder.Property(x => x.State).HasMaxLength(20).HasDefaultValue("disponible").IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TransitOfficeId, x.Plate })
            .IsUnique()
            .HasDatabaseName("uq_plate_range_details_office_plate");
        builder.HasIndex(x => new { x.TenantId, x.State })
            .HasDatabaseName("ix_plate_range_details_tenant_state");
    }
}
