using Flit.Infrastructure.Persistence.Entities.Catalogs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Catalogs;

internal sealed class TransitOfficeConfiguration : IEntityTypeConfiguration<TransitOffice>
{
    public void Configure(EntityTypeBuilder<TransitOffice> builder)
    {
        builder.ToTable("transit_offices", "catalogs");
        builder.HasKey(x => x.Id).HasName("pk_transit_offices");
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(10).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DepartmentCode).HasColumnName("department_code").HasMaxLength(2).IsRequired();
        builder.Property(x => x.CityCode).HasColumnName("city_code").HasMaxLength(5).IsRequired();
        builder.Property(x => x.CityName).HasColumnName("city_name").HasMaxLength(100);
        builder.Property(x => x.DepartmentName).HasColumnName("department_name").HasMaxLength(100);
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();

        // Quipux (HU #10710). Nullable a propósito: se carga a mano y "no lo sé" debe distinguirse
        // de "vacío" — sin DIVIPO la secretaría no es elegible para radicar.
        builder.Property(x => x.DivipoCode).HasColumnName("divipo_code").HasMaxLength(20);
        builder.Property(x => x.QuipuxRegistration).HasColumnName("quipux_registration").IsRequired().HasDefaultValue(false);
        builder.Property(x => x.QuipuxTransfer).HasColumnName("quipux_transfer").IsRequired().HasDefaultValue(false);
        builder.Property(x => x.QuipuxOther).HasColumnName("quipux_other").IsRequired().HasDefaultValue(false);

        // Soporta el barrido de elegibilidad: sin DIVIPO no se radica, así que el índice parcial
        // solo cubre las secretarías que sí están parametrizadas (hoy 6 de 317).
        builder.HasIndex(x => x.DivipoCode)
            .HasDatabaseName("ix_transit_offices_divipo_code")
            .HasFilter("divipo_code IS NOT NULL");
    }
}
