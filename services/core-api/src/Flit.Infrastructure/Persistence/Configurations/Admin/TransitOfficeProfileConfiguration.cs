using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TransitOfficeProfileConfiguration : IEntityTypeConfiguration<TransitOfficeProfile>
{
    public void Configure(EntityTypeBuilder<TransitOfficeProfile> builder)
    {
        builder.ToTable("transit_office_profiles", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("uq_transit_office_profiles_tenant_id");

        builder.Property(x => x.TransitOfficeId).IsRequired();
        // Refactor adminOT: una oficina física del catálogo = un solo tenant OT. Antes de
        // este índice, nada impedía crear dos tenants OT apuntando a la misma oficina; la
        // regla ya se valida en CreateTransitOfficeHandler, pero se blinda también en BD.
        builder.HasIndex(x => x.TransitOfficeId)
            .IsUnique()
            .HasDatabaseName("uq_transit_office_profiles_transit_office_id");
        builder.Property(x => x.OperationMode)
            .HasMaxLength(20)
            .HasDefaultValue("dashboard")
            .IsRequired();
        builder.Property(x => x.QuipuxReadOnly).HasDefaultValue(false);
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
