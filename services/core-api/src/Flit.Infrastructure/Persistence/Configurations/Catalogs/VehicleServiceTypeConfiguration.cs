using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Catalogs;

internal sealed class VehicleServiceTypeConfiguration : IEntityTypeConfiguration<VehicleServiceType>
{
    public void Configure(EntityTypeBuilder<VehicleServiceType> builder)
    {
        builder.ToTable("vehicle_service_types", "catalogs");
        builder.HasKey(x => x.Id).HasName("pk_vehicle_service_types");

        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Code)
            .HasColumnName("code")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(x => x.SortOrder)
            .HasColumnName("sort_order")
            .IsRequired();

        builder.Property(x => x.IsActive)
            .HasColumnName("is_active")
            .IsRequired()
            .HasDefaultValue(true);

        builder.Property(x => x.ExternalRefs)
            .HasColumnName("external_refs")
            .HasColumnType("jsonb")
            .IsRequired()
            .HasDefaultValueSql("'{}'::jsonb");

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        builder.HasIndex(x => x.Code)
            .IsUnique()
            .HasDatabaseName("uq_vehicle_service_types_code");

        builder.HasIndex(x => x.SortOrder)
            .HasDatabaseName("ix_vehicle_service_types_active_sort")
            .HasFilter("deleted_at IS NULL AND is_active = true");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
