using Flit.Infrastructure.Persistence.Entities.Platform;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Platform;

/// <summary>
/// Mapeo EF Core de <c>platform.tenant_product_credentials</c> (ADR-0092). El DDL lo crea la
/// migración embebida; entidad <c>ExcludeFromMigrations</c>. Sin trigger de audit_log (excepciones A6/A16).
/// </summary>
internal sealed class TenantProductCredentialConfiguration : IEntityTypeConfiguration<TenantProductCredentialEntity>
{
    public void Configure(EntityTypeBuilder<TenantProductCredentialEntity> builder)
    {
        builder.ToTable("tenant_product_credentials", SchemaNames.Platform, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_tenant_product_credentials_row_version");
        });

        builder.HasKey(x => x.Id).HasName("pk_tenant_product_credentials");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProductCode).HasColumnName("product_code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.CredentialCode).HasColumnName("credential_code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.SecretProtected).HasColumnName("secret_protected").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        builder.HasIndex(x => new { x.TenantId, x.ProductCode, x.CredentialCode }, "uq_tenant_product_credentials_tenant_product_credential")
            .HasDatabaseName("uq_tenant_product_credentials_tenant_product_credential")
            .IsUnique();
        builder.HasIndex(x => x.ProductCode, "ix_tenant_product_credentials_product_code")
            .HasDatabaseName("ix_tenant_product_credentials_product_code");
    }
}
