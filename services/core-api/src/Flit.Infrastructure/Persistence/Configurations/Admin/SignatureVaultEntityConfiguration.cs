using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core del baúl de firmas (HU #10642, ADR-0025). El DDL lo gestiona la migración SQL
/// cruda (32-HU10642-signature-vault.sql): índice único parcial (una firma 'activa' por
/// (tenant, NIT, documento)), CHECKs (estado + vigencia), FK opcional al mandatario, RLS y
/// triggers (row_version + audit) que el MigrationBuilder no modela. La entidad se excluye de
/// migraciones (mismo patrón que <see cref="ProcedureInstancePrendaConfiguration"/> /
/// ProcedureInstance); el snapshot la refleja para el modelo EF y se declaran los triggers para
/// que EF emita UPDATE compatible con row_version como concurrency token.
/// </summary>
internal sealed class SignatureVaultEntityConfiguration : IEntityTypeConfiguration<SignatureVaultEntity>
{
    public void Configure(EntityTypeBuilder<SignatureVaultEntity> builder)
    {
        builder.ToTable("signature_vault", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_signature_vault_row_version");
            t.HasTrigger("tr_signature_vault_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DocumentNumber).HasMaxLength(20).IsRequired();
        // NIT deprecado (HU #10930, Feature #10929): nullable, ya no obligatorio.
        builder.Property(x => x.NitEmpresa).HasMaxLength(20);
        builder.Property(x => x.FullName).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SignatureHash).HasMaxLength(64).IsRequired();
        // Código alfanumérico digitado por el usuario (distinto del SHA-256 del artefacto). Opcional.
        builder.Property(x => x.CodigoHash).HasMaxLength(100);
        builder.Property(x => x.StoragePath).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.StorageSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Estado).HasMaxLength(20).IsRequired().HasDefaultValue("activa");
        builder.Property(x => x.VigenciaDesde).IsRequired();
        builder.Property(x => x.VigenciaHasta).IsRequired();
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        // Consulta de consumo (deprecada) por NIT: se conserva durante la transición (HU #10930).
        builder.HasIndex(x => new { x.TenantId, x.NitEmpresa, x.Estado })
            .HasDatabaseName("ix_signature_vault_tenant_id_nit_empresa_estado");

        // Consulta de consumo por persona: firma vigente por (tenant, documento) filtrando por estado.
        builder.HasIndex(x => new { x.TenantId, x.DocumentNumber, x.Estado })
            .HasDatabaseName("ix_signature_vault_tenant_document_estado");

        // Invariante (HU #10930, Feature #10929): a lo sumo UNA firma 'activa' por (tenant, documento).
        builder.HasIndex(x => new { x.TenantId, x.DocumentNumber })
            .IsUnique()
            .HasFilter("estado = 'activa'")
            .HasDatabaseName("uq_signature_vault_activa");

        builder.HasIndex(x => x.MandateSignerId)
            .HasDatabaseName("ix_signature_vault_mandate_signer_id");

        // FK opcional al mandatario (ON DELETE SET NULL): el apoderado del baúl puede coincidir
        // con un firmante de mandato, pero el baúl no depende de él (ADR-0025 §1).
        builder.HasOne<MandateSigner>()
            .WithMany()
            .HasForeignKey(x => x.MandateSignerId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_signature_vault_mandate_signers");
    }
}
