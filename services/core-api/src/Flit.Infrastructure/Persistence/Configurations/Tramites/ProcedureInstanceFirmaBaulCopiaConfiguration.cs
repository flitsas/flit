using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Copia de la firma de baúl tomada al radicar. La tabla (con su RLS, triggers e índices parciales) la crea el DDL
/// <c>195-firma-baul-copia-al-radicar.sql</c>, así que el modelo se excluye de las migraciones automáticas: EF solo
/// necesita leerla y escribirla. Sin navegaciones: se accede por <c>IProcedureInstanceRepository</c>.
/// </summary>
internal sealed class ProcedureInstanceFirmaBaulCopiaConfiguration
    : IEntityTypeConfiguration<ProcedureInstanceFirmaBaulCopia>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceFirmaBaulCopia> builder)
    {
        builder.ToTable(
            "procedure_instance_signature_vault_copies", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id).HasName("pk_procedure_instance_signature_vault_copies");
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.SlotKey).HasColumnName("slot_key").HasMaxLength(40).IsRequired();
        builder.Property(x => x.PartyRole).HasColumnName("party_role").HasMaxLength(20).IsRequired();
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasMaxLength(10).IsRequired();
        builder.Property(x => x.DocumentNumber).HasColumnName("document_number").HasMaxLength(40).IsRequired();
        builder.Property(x => x.SignatureVaultId).HasColumnName("signature_vault_id").IsRequired();
        builder.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.SignatureHash).HasColumnName("signature_hash").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CodigoHash).HasColumnName("codigo_hash").HasMaxLength(100);
        builder.Property(x => x.VigenciaDesde).HasColumnName("vigencia_desde").IsRequired();
        builder.Property(x => x.VigenciaHasta).HasColumnName("vigencia_hasta").IsRequired();
        builder.Property(x => x.CapturedAt).HasColumnName("captured_at").IsRequired();
        builder.Property(x => x.StoragePath).HasColumnName("storage_path").IsRequired();
        builder.Property(x => x.StorageSha256).HasColumnName("storage_sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.SourceStoragePath).HasColumnName("source_storage_path");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_procedure_instance_signature_vault_copies_instance");
    }
}
