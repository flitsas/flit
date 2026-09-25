using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del dominio dedicado de la red (HU #12416, ADR-0060 D1). El DDL lo gestiona la
/// migración SQL cruda (<c>116-HU12416-tenant-domains.sql</c>): CHECKs de estado y de formato del
/// host, índices únicos parciales, disparador de tipo MARCA_BLANCA, RLS y triggers (row_version +
/// audit). Entidad <c>ExcludeFromMigrations</c> (patrón <see cref="TenantBrandingConfiguration"/>).
/// </summary>
internal sealed class TenantDomainConfiguration : IEntityTypeConfiguration<TenantDomainEntity>
{
    public void Configure(EntityTypeBuilder<TenantDomainEntity> builder)
    {
        builder.ToTable("tenant_domains", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_tenant_domains_marca_blanca");
            t.HasTrigger("tr_tenant_domains_row_version");
            t.HasTrigger("tr_tenant_domains_audit");
        });

        builder.HasKey(x => x.Id).HasName("pk_tenant_domains");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.Host).HasColumnName("host").HasMaxLength(253).IsRequired();
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(40).HasDefaultValue("HUB").IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20)
            .HasDefaultValue(TenantDomainStatuses.Pending).IsRequired();
        builder.Property(x => x.VerificationToken).HasColumnName("verification_token").HasMaxLength(64).IsRequired();
        builder.Property(x => x.VerifiedAt).HasColumnName("verified_at");
        builder.Property(x => x.ActivatedAt).HasColumnName("activated_at");
        builder.Property(x => x.FailedAt).HasColumnName("failed_at");
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.CertificateIssuedAt).HasColumnName("certificate_issued_at");
        builder.Property(x => x.CertificateExpiresAt).HasColumnName("certificate_expires_at");
        builder.Property(x => x.LastCheckedAt).HasColumnName("last_checked_at");
        builder.Property(x => x.NextCheckAt).HasColumnName("next_check_at");
        builder.Property(x => x.CheckAttempts).HasColumnName("check_attempts").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.GraceUntil).HasColumnName("grace_until");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");
        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();

        // Reflejan los índices del DDL (el modelo no los crea): un dominio vigente por cabeza, un host
        // vigente en la plataforma, token jamás reutilizado, y los de lectura del resolutor y del job.
        // El nombre va en HasIndex(…, name) (dos índices sobre la misma propiedad —único parcial y de
        // cobertura— solo coexisten en el modelo con nombre distinto) Y en HasDatabaseName, porque el
        // convenio snake_case reescribiría el nombre de BD (ix_tenant_domains_host1).
        // HU #12968: un dominio vigente por (empresa, propósito), no por empresa.
        builder.HasIndex(x => new { x.TenantId, x.Purpose }, "uq_tenant_domains_tenant_purpose")
            .HasDatabaseName("uq_tenant_domains_tenant_purpose")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => x.Host, "uq_tenant_domains_host")
            .HasDatabaseName("uq_tenant_domains_host")
            .IsUnique()
            .HasFilter("deleted_at IS NULL");
        builder.HasIndex(x => x.VerificationToken, "uq_tenant_domains_verification_token")
            .HasDatabaseName("uq_tenant_domains_verification_token")
            .IsUnique();
        builder.HasIndex(x => x.TenantId, "ix_tenant_domains_tenant_id")
            .HasDatabaseName("ix_tenant_domains_tenant_id");
        builder.HasIndex(x => x.Host, "ix_tenant_domains_host_active")
            .HasDatabaseName("ix_tenant_domains_host_active")
            .HasFilter("status = 'active' AND deleted_at IS NULL");
        builder.HasIndex(x => x.NextCheckAt, "ix_tenant_domains_next_check")
            .HasDatabaseName("ix_tenant_domains_next_check")
            .HasFilter("deleted_at IS NULL AND status IN ('pending', 'failed', 'active')");
    }
}

/// <summary>
/// Mapeo keyless de la vista <c>admin.v_active_network_domains</c> (HU #12416, ADR-0060 D2). La vista
/// la crea el DDL 116; <c>ToView</c> la deja fuera de las migraciones generadas por el modelo.
/// </summary>
internal sealed class ActiveNetworkDomainViewConfiguration : IEntityTypeConfiguration<ActiveNetworkDomainView>
{
    public void Configure(EntityTypeBuilder<ActiveNetworkDomainView> builder)
    {
        builder.HasNoKey();
        builder.ToView("v_active_network_domains", SchemaNames.Admin);

        builder.Property(x => x.Host).HasColumnName("host").HasMaxLength(253).IsRequired();
        builder.Property(x => x.HeadTenantId).HasColumnName("head_tenant_id").IsRequired();
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(40).IsRequired();
    }
}
