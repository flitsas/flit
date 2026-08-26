using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de la versión de documento personalizado (HU #11313, ADR-0042). El DDL lo gestiona la
/// migración SQL cruda: CHECK del vocabulario cerrado, único parcial de una sola activa por
/// <c>(tenant_id, document_type)</c>, único <c>(tenant_id, document_type, version)</c>, RLS y triggers
/// (row_version + audit). Entidad <c>ExcludeFromMigrations</c> (patrón de <c>CompanyDeedConfiguration</c>).
/// </summary>
internal sealed class CompanyPersonalizedDocumentConfiguration : IEntityTypeConfiguration<CompanyPersonalizedDocumentEntity>
{
    public void Configure(EntityTypeBuilder<CompanyPersonalizedDocumentEntity> builder)
    {
        builder.ToTable("company_personalized_documents", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_company_personalized_documents_row_version");
            t.HasTrigger("tr_company_personalized_documents_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.Filename).HasColumnName("filename").HasMaxLength(500).IsRequired();
        builder.Property(x => x.StoragePath).HasColumnName("storage_path").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.StorageSha256).HasColumnName("storage_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.PageCount).HasColumnName("page_count");
        builder.Property(x => x.Notes).HasColumnName("notes").HasMaxLength(1000);
        builder.Property(x => x.ActivatedAt).HasColumnName("activated_at");
        builder.Property(x => x.ActivatedBy).HasColumnName("activated_by");
        builder.Property(x => x.DeactivatedAt).HasColumnName("deactivated_at");
        builder.Property(x => x.DeactivatedBy).HasColumnName("deactivated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.DocumentType })
            .HasDatabaseName("ix_company_personalized_documents_tenant_type");

        builder.HasIndex(x => new { x.TenantId, x.DocumentType, x.Version })
            .IsUnique()
            .HasDatabaseName("uq_company_personalized_documents_tenant_type_version");
    }
}
