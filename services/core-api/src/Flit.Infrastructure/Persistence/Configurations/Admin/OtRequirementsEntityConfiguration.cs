using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class OtRequirementsEntityConfiguration : IEntityTypeConfiguration<OtRequirementsEntity>
{
    public void Configure(EntityTypeBuilder<OtRequirementsEntity> builder)
    {
        builder.ToTable("ot_requirements", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("uq_ot_requirements_tenant_id");

        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.HasIndex(x => x.TransitOfficeId)
            .IsUnique()
            .HasDatabaseName("uq_ot_requirements_transit_office_id");

        builder.Property(x => x.RequiresRnmc).HasDefaultValue(false);
        builder.Property(x => x.AllowPlatePreassign).HasDefaultValue(false);
        builder.Property(x => x.IdentityValidationEnabled).HasDefaultValue(true);
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
