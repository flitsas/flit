using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF Core del registro mercantil por trámite y NIT (HU #11302, ADR-0041). DDL en
/// <c>59-certificaciones-externas.sql</c>; entidad excluida de migraciones por los CHECKs de
/// vocabulario, la RLS y los triggers.
/// </summary>
internal sealed class CompanyRegistrationConfiguration : IEntityTypeConfiguration<CompanyRegistration>
{
    public void Configure(EntityTypeBuilder<CompanyRegistration> builder)
    {
        builder.ToTable("company_registrations", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_company_registrations_row_version");
            t.HasTrigger("tr_company_registrations_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.Nit).HasColumnName("nit").HasMaxLength(20).IsRequired();

        builder.Property(x => x.BusinessName).HasColumnName("business_name").HasMaxLength(400);
        builder.Property(x => x.BusinessNameRaw).HasColumnName("business_name_raw").HasColumnType("text");

        builder.Property(x => x.RegistrationNumber).HasColumnName("registration_number").HasMaxLength(60);
        builder.Property(x => x.RegistrationNumberRaw).HasColumnName("registration_number_raw").HasColumnType("text");

        builder.Property(x => x.RegistrationStatus).HasColumnName("registration_status").HasMaxLength(12)
            .IsRequired().HasDefaultValue("unknown");
        builder.Property(x => x.RegistrationStatusRaw).HasColumnName("registration_status_raw").HasColumnType("text");

        builder.Property(x => x.RegisteredOn).HasColumnName("registered_on");
        builder.Property(x => x.RegisteredOnRaw).HasColumnName("registered_on_raw").HasColumnType("text");

        builder.Property(x => x.RenewedOn).HasColumnName("renewed_on");
        builder.Property(x => x.RenewedOnRaw).HasColumnName("renewed_on_raw").HasColumnType("text");

        builder.Property(x => x.ChamberOfCommerce).HasColumnName("chamber_of_commerce").HasMaxLength(400);
        builder.Property(x => x.ChamberOfCommerceRaw).HasColumnName("chamber_of_commerce_raw").HasColumnType("text");

        builder.Property(x => x.Category).HasColumnName("category").HasMaxLength(400);
        builder.Property(x => x.CategoryRaw).HasColumnName("category_raw").HasColumnType("text");

        builder.Property(x => x.Address).HasColumnName("address").HasMaxLength(400);
        builder.Property(x => x.AddressRaw).HasColumnName("address_raw").HasColumnType("text");

        builder.Property(x => x.City).HasColumnName("city").HasMaxLength(400);
        builder.Property(x => x.CityRaw).HasColumnName("city_raw").HasColumnType("text");

        builder.Property(x => x.LegalRepresentatives).HasColumnName("legal_representatives")
            .HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'[]'");

        builder.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(12).IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(40).IsRequired();
        builder.Property(x => x.ObservedAt).HasColumnName("observed_at").IsRequired();
        builder.Property(x => x.RawPayloadId).HasColumnName("raw_payload_id");
        builder.Property(x => x.MapperVersion).HasColumnName("mapper_version").HasMaxLength(20)
            .IsRequired().HasDefaultValue("unknown");
        builder.Property(x => x.NormalizationIssues).HasColumnName("normalization_issues")
            .HasColumnType("jsonb").IsRequired().HasDefaultValueSql("'[]'");

        builder.Property(x => x.FrozenAt).HasColumnName("frozen_at");

        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(x => new { x.ProcedureInstanceId, x.Nit })
            .IsUnique()
            .HasDatabaseName("uq_company_registrations_instance_nit");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_company_registrations_tenant_instance");

        builder.HasIndex(x => x.RawPayloadId)
            .HasDatabaseName("ix_company_registrations_raw_payload");

        builder.HasIndex(x => new { x.ProviderKey, x.MapperVersion })
            .HasDatabaseName("ix_company_registrations_mapper_version");
    }
}
