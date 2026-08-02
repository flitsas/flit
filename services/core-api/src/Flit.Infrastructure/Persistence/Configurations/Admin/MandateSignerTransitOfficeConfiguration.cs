using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del puente mandatario ↔ organismo de tránsito (HU #11201, Feature #11190). El DDL lo
/// gestiona la migración SQL cruda (48-HU11201): único parcial (mandatario, organismo) sobre activos,
/// FK en cascada al mandatario e índice de lectura por organismo. Entidad ExcludeFromMigrations
/// (mismo patrón que <see cref="LegalRepresentativeCompanyConfiguration"/>).
/// </summary>
internal sealed class MandateSignerTransitOfficeConfiguration
    : IEntityTypeConfiguration<MandateSignerTransitOffice>
{
    public void Configure(EntityTypeBuilder<MandateSignerTransitOffice> builder)
    {
        builder.ToTable("mandate_signer_transit_offices", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.MandateSignerId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.SignsPhysically).HasColumnName("signs_physically")
            .IsRequired().HasDefaultValue(false);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.MandateSignerId, x.TransitOfficeId })
            .IsUnique()
            .HasDatabaseName("uq_mandate_signer_transit_offices_active")
            .HasFilter("is_active");

        builder.HasIndex(x => new { x.TransitOfficeId, x.IsActive })
            .HasDatabaseName("ix_mandate_signer_transit_offices_office");

        builder.HasOne<MandateSigner>()
            .WithMany()
            .HasForeignKey(x => x.MandateSignerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
