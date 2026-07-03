using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class IdentityValidationAuditEventConfiguration
    : IEntityTypeConfiguration<IdentityValidationAuditEvent>
{
    public void Configure(EntityTypeBuilder<IdentityValidationAuditEvent> builder)
    {
        builder.ToTable("identity_validation_audit", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.Stage).HasColumnName("stage").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(40).IsRequired();

        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id");
        builder.Property(x => x.ValidationId).HasColumnName("validation_id");
        builder.Property(x => x.KyverumVerificationId).HasColumnName("kyverum_verification_id").HasMaxLength(200);
        builder.Property(x => x.PartyRole).HasColumnName("party_role").HasMaxLength(20);

        builder.Property(x => x.HttpStatus).HasColumnName("http_status");
        builder.Property(x => x.SignaturePresent).HasColumnName("signature_present");
        builder.Property(x => x.SecretPresent).HasColumnName("secret_present");
        builder.Property(x => x.DecryptOk).HasColumnName("decrypt_ok");
        builder.Property(x => x.ProviderStatus).HasColumnName("provider_status").HasMaxLength(60);
        builder.Property(x => x.ErrorType).HasColumnName("error_type").HasMaxLength(120);
        builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(1000);
        builder.Property(x => x.Detail).HasColumnName("detail").HasColumnType("jsonb");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        // Historia de una validación: WHERE validation_id = X ORDER BY occurred_at.
        builder.HasIndex(x => new { x.ValidationId, x.OccurredAt })
            .HasDatabaseName("ix_identity_validation_audit_validation");

        // Correlación por id de Kyverum (cuando no se conoce nuestro validation_id).
        builder.HasIndex(x => x.KyverumVerificationId)
            .HasDatabaseName("ix_identity_validation_audit_kyverum")
            .HasFilter("kyverum_verification_id IS NOT NULL");

        // Barrido reciente por tenant (monitoreo).
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt })
            .HasDatabaseName("ix_identity_validation_audit_tenant");
    }
}
