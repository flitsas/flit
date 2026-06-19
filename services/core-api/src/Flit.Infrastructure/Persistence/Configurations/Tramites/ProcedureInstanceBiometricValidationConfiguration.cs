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

        builder.Property(x => x.Parte).HasColumnName("parte").HasMaxLength(20);
        builder.Property(x => x.Nombre).HasColumnName("nombre").HasMaxLength(200).IsRequired();
        builder.Property(x => x.TipoDoc).HasColumnName("tipo_doc").HasMaxLength(20).IsRequired();
        builder.Property(x => x.Documento).HasColumnName("documento").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Email).HasColumnName("email").HasMaxLength(320).IsRequired();
        builder.Property(x => x.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired().HasDefaultValue("enviado");
        builder.Property(x => x.TokenHash).HasColumnName("token_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(x => x.Intentos).HasColumnName("intentos").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.MaxIntentos).HasColumnName("max_intentos").IsRequired().HasDefaultValue(5);
        builder.Property(x => x.Score).HasColumnName("score");
        builder.Property(x => x.Detalle).HasColumnName("detalle").HasColumnType("jsonb");
        builder.Property(x => x.FotoRostroPath).HasColumnName("foto_rostro_path").HasMaxLength(1000);
        builder.Property(x => x.FotoCedulaFrontalPath).HasColumnName("foto_cedula_frontal_path").HasMaxLength(1000);
        builder.Property(x => x.FotoCedulaReversoPath).HasColumnName("foto_cedula_reverso_path").HasMaxLength(1000);
        builder.Property(x => x.ValidadoAt).HasColumnName("validado_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

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
