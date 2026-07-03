using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceBiometricValidationConfiguration
    : IEntityTypeConfiguration<ProcedureInstanceBiometricValidation>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceBiometricValidation> builder)
    {
        builder.ToTable("procedure_instance_biometric_validations", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_biometric_validations_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.PartyRole).HasColumnName("party_role").HasMaxLength(20);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(20).IsRequired();
        builder.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired().HasDefaultValue("enviado");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.Attempts).HasColumnName("attempts").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").IsRequired().HasDefaultValue(5);
        // Clave del último intento contado (dedup del conteo de reintentos Kyverum). Texto tomado del CUERPO del
        // webhook (closedAt/ts/requestId); se compara exacto, sin parsear.
        builder.Property(x => x.LastAttemptAt).HasColumnName("last_attempt_at").HasMaxLength(40);
        // Sondeos del worker de reconciliación en la ventana de espera actual (presupuesto: se calla al llegar a 3).
        builder.Property(x => x.ReconcilePollCount).HasColumnName("reconcile_poll_count").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.Score).HasColumnName("score");
        builder.Property(x => x.Detail).HasColumnName("detail").HasColumnType("jsonb");
        builder.Property(x => x.FacePhotoPath).HasColumnName("face_photo_path").HasMaxLength(1000);
        builder.Property(x => x.IdFrontPhotoPath).HasColumnName("id_front_photo_path").HasMaxLength(1000);
        builder.Property(x => x.IdBackPhotoPath).HasColumnName("id_back_photo_path").HasMaxLength(1000);
        builder.Property(x => x.ValidatedAt).HasColumnName("validated_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // HU #10233 — Kyverum Verify.
        builder.Property(x => x.Provider).HasColumnName("provider").HasMaxLength(20).IsRequired().HasDefaultValue("mock");
        builder.Property(x => x.KyverumVerificationId).HasColumnName("kyverum_verification_id").HasMaxLength(200);
        builder.Property(x => x.CaptureUrl).HasColumnName("capture_url").HasMaxLength(2000);
        builder.Property(x => x.WebhookSecretEncrypted).HasColumnName("webhook_secret_encrypted").HasMaxLength(2000);
        builder.Property(x => x.ProviderStatus).HasColumnName("provider_status").HasMaxLength(40);
        builder.Property(x => x.ProviderPayload).HasColumnName("provider_payload").HasColumnType("jsonb");
        // HU #10488 — serie/hash del certificado biométrico (firmaSerie de Kyverum) que alimenta el sello del FUR.
        builder.Property(x => x.CertificateHash).HasColumnName("certificate_hash").HasMaxLength(200);

        // HU #10350 — fecha de fin de vigencia: columna NORMAL que estampa el código al aprobar
        // (validado_at + 30 días, medianoche Colombia). Los "días restantes" NO se persisten: se calculan
        // al leer. (Antes era mantenida por un trigger; se quitó para no meter lógica en la BD.)
        builder.Property(x => x.ValidUntil).HasColumnName("valid_until");

        builder.HasIndex(x => x.KyverumVerificationId)
            .IsUnique()
            .HasDatabaseName("uq_procedure_instance_biometric_validations_kyverum_verification_id")
            .HasFilter("kyverum_verification_id IS NOT NULL");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_procedure_instance_biometric_validations_tenant_id_instance");

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasDatabaseName("uq_procedure_instance_biometric_validations_token_hash");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.BiometricValidations)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_biometric_validations_procedure_instances");
    }
}
