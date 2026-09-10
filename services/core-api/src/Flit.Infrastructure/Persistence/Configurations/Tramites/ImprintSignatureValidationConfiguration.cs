using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #12148 — bitácora append-only de validaciones OT. DDL en
/// <c>100-imprint-signature-validations.sql</c> (ExcludeFromMigrations).
/// </summary>
internal sealed class ImprintSignatureValidationConfiguration : IEntityTypeConfiguration<ImprintSignatureValidation>
{
    public void Configure(EntityTypeBuilder<ImprintSignatureValidation> builder)
    {
        builder.ToTable("imprint_signature_validations", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.VehicleSignatureImprintId).HasColumnName("vehicle_signature_imprint_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.Placa).HasColumnName("placa").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ValidatedBy).HasColumnName("validated_by").IsRequired();
        builder.Property(x => x.ValidatedAt).HasColumnName("validated_at").IsRequired();
        builder.Property(x => x.Result).HasColumnName("result").HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason");

        builder.HasIndex(x => new { x.TenantId, x.ValidatedAt })
            .HasDatabaseName("ix_imprint_signature_validations_tenant_validated_at");
        builder.HasIndex(x => x.VehicleSignatureImprintId)
            .HasDatabaseName("ix_imprint_signature_validations_imprint");
    }
}
