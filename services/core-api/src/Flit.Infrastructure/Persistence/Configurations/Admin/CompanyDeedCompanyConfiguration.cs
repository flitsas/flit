using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del puente escritura ↔ compañía representada (HU #10900, ADR-0033). El DDL lo
/// gestiona la migración SQL cruda: índice único (escritura, compañía), FK en cascada a la escritura,
/// FK a la compañía representada y RLS transitiva. Entidad ExcludeFromMigrations.
/// </summary>
internal sealed class CompanyDeedCompanyConfiguration
    : IEntityTypeConfiguration<CompanyDeedCompanyEntity>
{
    public void Configure(EntityTypeBuilder<CompanyDeedCompanyEntity> builder)
    {
        builder.ToTable("company_deed_companies", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.DeedId).IsRequired();
        builder.Property(x => x.RepresentedCompanyId).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.DeedId, x.RepresentedCompanyId })
            .IsUnique()
            .HasDatabaseName("uq_company_deed_companies_deed_company");
        builder.HasIndex(x => x.RepresentedCompanyId)
            .HasDatabaseName("ix_company_deed_companies_represented_company_id");

        builder.HasOne<CompanyDeedEntity>()
            .WithMany()
            .HasForeignKey(x => x.DeedId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_company_deed_companies_deed");

        builder.HasOne<RepresentedCompanyEntity>()
            .WithMany()
            .HasForeignKey(x => x.RepresentedCompanyId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_company_deed_companies_represented_company");
    }
}
