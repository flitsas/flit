using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF Core del histórico de pólizas de SOAT (HU #11302, ADR-0041). DDL en
/// <c>59-certificaciones-externas.sql</c>; la entidad se excluye de migraciones porque el índice
/// único de <c>is_current</c> es PARCIAL y los CHECKs de vocabulario no los modela el generador.
/// </summary>
internal sealed class VehicleSoatPolicyConfiguration : IEntityTypeConfiguration<VehicleSoatPolicy>
{
    public void Configure(EntityTypeBuilder<VehicleSoatPolicy> builder)
    {
        builder.ToTable("vehicle_soat_policies", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_vehicle_soat_policies_row_version");
            t.HasTrigger("tr_vehicle_soat_policies_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.NaturalKey).HasColumnName("natural_key").HasMaxLength(140).IsRequired();

        builder.Property(x => x.PolicyNumber).HasColumnName("policy_number").HasMaxLength(60);
        builder.Property(x => x.PolicyNumberRaw).HasColumnName("policy_number_raw").HasColumnType("text");

        builder.Property(x => x.InsurerName).HasColumnName("insurer_name").HasMaxLength(400);
        builder.Property(x => x.InsurerNameRaw).HasColumnName("insurer_name_raw").HasColumnType("text");

        builder.Property(x => x.IssuedOn).HasColumnName("issued_on");
        builder.Property(x => x.IssuedOnRaw).HasColumnName("issued_on_raw").HasColumnType("text");

        builder.Property(x => x.ValidFrom).HasColumnName("valid_from");
        builder.Property(x => x.ValidFromRaw).HasColumnName("valid_from_raw").HasColumnType("text");

        builder.Property(x => x.ValidUntil).HasColumnName("valid_until");
        builder.Property(x => x.ValidUntilRaw).HasColumnName("valid_until_raw").HasColumnType("text");

        builder.Property(x => x.VigencyStatus).HasColumnName("vigency_status").HasMaxLength(12)
            .IsRequired().HasDefaultValue("unknown");
        builder.Property(x => x.VigencyStatusRaw).HasColumnName("vigency_status_raw").HasColumnType("text");

        builder.Property(x => x.IsCurrent).HasColumnName("is_current").IsRequired().HasDefaultValue(false);

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

        // Excepción A11 documentada en el DDL: la unicidad natural es por instancia (que ya determina
        // el tenant); anteponer tenant_id lo haría inútil para el upsert.
        builder.HasIndex(x => new { x.ProcedureInstanceId, x.NaturalKey })
            .IsUnique()
            .HasDatabaseName("uq_vehicle_soat_policies_instance_natural");

        builder.HasIndex(x => x.ProcedureInstanceId)
            .IsUnique()
            .HasFilter("is_current")
            .HasDatabaseName("uq_vehicle_soat_policies_current");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_vehicle_soat_policies_tenant_instance");

        builder.HasIndex(x => x.RawPayloadId)
            .HasDatabaseName("ix_vehicle_soat_policies_raw_payload");

        builder.HasIndex(x => new { x.ProviderKey, x.MapperVersion })
            .HasDatabaseName("ix_vehicle_soat_policies_mapper_version");
    }
}
