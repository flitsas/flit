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
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
    }
}
