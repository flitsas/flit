using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF Core del histórico de revisiones técnico-mecánicas (HU #11302, ADR-0041). DDL en
/// <c>59-certificaciones-externas.sql</c>; entidad excluida de migraciones por el índice único parcial
/// de <c>is_current</c> y los CHECKs de vocabulario.
/// </summary>
internal sealed class VehicleRtmInspectionConfiguration : IEntityTypeConfiguration<VehicleRtmInspection>
{
    public void Configure(EntityTypeBuilder<VehicleRtmInspection> builder)
    {
        builder.ToTable("vehicle_rtm_inspections", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_vehicle_rtm_inspections_row_version");
            t.HasTrigger("tr_vehicle_rtm_inspections_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.NaturalKey).HasColumnName("natural_key").HasMaxLength(140).IsRequired();

        builder.Property(x => x.CertificateNumber).HasColumnName("certificate_number").HasMaxLength(60);
        builder.Property(x => x.CertificateNumberRaw).HasColumnName("certificate_number_raw").HasColumnType("text");

        builder.Property(x => x.CdaName).HasColumnName("cda_name").HasMaxLength(400);
        builder.Property(x => x.CdaNameRaw).HasColumnName("cda_name_raw").HasColumnType("text");

        builder.Property(x => x.IssuedOn).HasColumnName("issued_on");
        builder.Property(x => x.IssuedOnRaw).HasColumnName("issued_on_raw").HasColumnType("text");

        builder.Property(x => x.ValidFrom).HasColumnName("valid_from");
        builder.Property(x => x.ValidFromRaw).HasColumnName("valid_from_raw").HasColumnType("text");

        builder.Property(x => x.ValidUntil).HasColumnName("valid_until");
        builder.Property(x => x.ValidUntilRaw).HasColumnName("valid_until_raw").HasColumnType("text");

        builder.Property(x => x.VigencyStatus).HasColumnName("vigency_status").HasMaxLength(12)
            .IsRequired().HasDefaultValue("unknown");
        builder.Property(x => x.VigencyStatusRaw).HasColumnName("vigency_status_raw").HasColumnType("text");

        builder.Property(x => x.InspectionType).HasColumnName("inspection_type").HasMaxLength(60);

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

        builder.HasIndex(x => new { x.ProcedureInstanceId, x.NaturalKey })
            .IsUnique()
            .HasDatabaseName("uq_vehicle_rtm_inspections_instance_natural");

        builder.HasIndex(x => x.ProcedureInstanceId)
            .IsUnique()
            .HasFilter("is_current")
            .HasDatabaseName("uq_vehicle_rtm_inspections_current");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_vehicle_rtm_inspections_tenant_instance");

        builder.HasIndex(x => x.RawPayloadId)
            .HasDatabaseName("ix_vehicle_rtm_inspections_raw_payload");

        builder.HasIndex(x => new { x.ProviderKey, x.MapperVersion })
            .HasDatabaseName("ix_vehicle_rtm_inspections_mapper_version");
    }
}
