using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class MandateSignerCompanyConfiguration : IEntityTypeConfiguration<MandateSignerCompany>
{
    public void Configure(EntityTypeBuilder<MandateSignerCompany> builder)
    {
        builder.ToTable("mandate_signer_companies", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.MandateSignerId).IsRequired();
        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.CompanyTenantId).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne<MandateSigner>()
            .WithMany()
            .HasForeignKey(x => x.MandateSignerId)
            .OnDelete(DeleteBehavior.Cascade);

        // MULTIPLICIDAD (ADR-0036, supersede la exclusividad de ADR-0023): una compañía puede tener
        // VARIOS mandatarios activos por OT, y un mandatario varias compañías. El índice único ahora
        // incluye mandate_signer_id (evita duplicar la MISMA asignación activa, no la excluye entre
        // mandatarios distintos). Sigue filtrado por is_active: al inactivar el mandatario sus filas
        // quedan is_active=false y dejan de contar.
        builder.HasIndex(x => new { x.TransitOfficeId, x.CompanyTenantId, x.MandateSignerId })
            .IsUnique()
            .HasDatabaseName("uq_mandate_signer_companies_active")
            .HasFilter("is_active");

        builder.HasIndex(x => x.MandateSignerId)
            .HasDatabaseName("ix_mandate_signer_companies_mandate_signer_id");
    }
}
