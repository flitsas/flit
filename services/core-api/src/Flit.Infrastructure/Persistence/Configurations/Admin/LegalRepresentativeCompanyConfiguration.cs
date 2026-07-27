using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del puente representante ↔ compañía representada (HU #10932, Feature #10929). El DDL
/// lo gestiona la migración SQL cruda (42-HU10932): índice único (representante, compañía), FK en
/// cascada al representante, FK a la compañía representada, RLS por tenant. Entidad
/// ExcludeFromMigrations (patrón de <see cref="CompanyDeedCompanyConfiguration"/>).
/// </summary>
internal sealed class LegalRepresentativeCompanyConfiguration
    : IEntityTypeConfiguration<LegalRepresentativeCompanyEntity>
{
    public void Configure(EntityTypeBuilder<LegalRepresentativeCompanyEntity> builder)
    {
        builder.ToTable("legal_representative_companies", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.RepresentativeId).IsRequired();
        builder.Property(x => x.RepresentedCompanyId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.RepresentativeId, x.RepresentedCompanyId })
            .IsUnique()
            .HasDatabaseName("uq_lrc_representative_company");
        builder.HasIndex(x => x.RepresentedCompanyId)
            .HasDatabaseName("ix_lrc_represented_company_id");
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_lrc_tenant_id");

        builder.HasOne<CompanyLegalRepresentativeEntity>()
            .WithMany()
            .HasForeignKey(x => x.RepresentativeId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_lrc_representative");

        builder.HasOne<RepresentedCompanyEntity>()
            .WithMany()
            .HasForeignKey(x => x.RepresentedCompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_lrc_represented_company");
    }
}
