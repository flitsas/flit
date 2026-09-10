using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del representante legal por compañía (HU #10900, ADR-0033). El DDL lo gestiona la
/// migración SQL cruda: índice único (tenant, compañía, documento), FK a la compañía representada,
/// FK opcional al baúl (ON DELETE SET NULL), RLS y triggers (row_version + audit). Entidad
/// ExcludeFromMigrations (patrón del baúl).
/// </summary>
internal sealed class CompanyLegalRepresentativeConfiguration
    : IEntityTypeConfiguration<CompanyLegalRepresentativeEntity>
{
    public void Configure(EntityTypeBuilder<CompanyLegalRepresentativeEntity> builder)
    {
        builder.ToTable("company_legal_representatives", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_company_legal_representatives_row_version");
            t.HasTrigger("tr_company_legal_representatives_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        // HU #10932: compañía primaria deprecada y nullable (persona puede existir sin NITs aún).
        builder.Property(x => x.RepresentedCompanyId).IsRequired(false);
        builder.Property(x => x.DocumentType).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DocumentNumber).HasMaxLength(20).IsRequired();
        builder.Property(x => x.FirstLastName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.SecondLastName).HasMaxLength(120);
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(200);
        builder.Property(x => x.Address).HasMaxLength(300);
        builder.Property(x => x.City).HasMaxLength(120);
        builder.Property(x => x.Phone).HasMaxLength(40);
        builder.Property(x => x.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        // HU #10932: el representante es la PERSONA (única por tenant+documento entre los activos); las
        // compañías se modelan en el puente legal_representative_companies. represented_company_id se
        // conserva como compañía primaria (nullable, deprecado).
        builder.HasIndex(x => new { x.TenantId, x.DocumentNumber })
            .IsUnique()
            .HasFilter("is_active")
            .HasDatabaseName("uq_company_legal_representatives_tenant_document");

        builder.HasIndex(x => x.RepresentedCompanyId)
            .HasDatabaseName("ix_company_legal_representatives_represented_company_id");
        builder.HasIndex(x => x.SignatureVaultId)
            .HasDatabaseName("ix_company_legal_representatives_signature_vault_id");
        builder.HasIndex(x => new { x.TenantId, x.DocumentType, x.DocumentNumber })
            .HasDatabaseName("ix_company_legal_representatives_tenant_document");

        builder.HasOne<RepresentedCompanyEntity>()
            .WithMany()
            .HasForeignKey(x => x.RepresentedCompanyId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_company_legal_representatives_represented_companies");

        // FK opcional al baúl (ON DELETE SET NULL): la firma del representante es su fila del baúl.
        builder.HasOne<SignatureVaultEntity>()
            .WithMany()
            .HasForeignKey(x => x.SignatureVaultId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_company_legal_representatives_signature_vault");
    }
}
