using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Catalogs;

internal sealed class VehicleBodyworkConfiguration : IEntityTypeConfiguration<VehicleBodywork>
{
    public void Configure(EntityTypeBuilder<VehicleBodywork> builder)
    {
        builder.ToTable("vehicle_bodyworks", "catalogs");
        builder.HasKey(x => x.Id).HasName("pk_vehicle_bodyworks");

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

        builder.Property(x => x.ClassVehicle)
            .HasColumnName("class_vehicle")
            .HasMaxLength(40);

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
            .HasDatabaseName("uq_vehicle_bodyworks_code");

        builder.HasIndex(x => x.Name)
            .HasDatabaseName("ix_vehicle_bodyworks_name_active")
            .HasFilter("deleted_at IS NULL AND is_active = true");

        builder.HasIndex(x => x.ClassVehicle)
            .HasDatabaseName("ix_vehicle_bodyworks_class_active")
            .HasFilter("deleted_at IS NULL AND is_active = true");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
