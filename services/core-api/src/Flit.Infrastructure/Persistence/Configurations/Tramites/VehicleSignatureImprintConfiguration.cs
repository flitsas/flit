using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #12116 — auditoría de impronta firmada. DDL en
/// <c>97-vehicle-signature-imprints.sql</c> (ExcludeFromMigrations).
/// </summary>
internal sealed class VehicleSignatureImprintConfiguration : IEntityTypeConfiguration<VehicleSignatureImprint>
{
    public void Configure(EntityTypeBuilder<VehicleSignatureImprint> builder)
    {
        builder.ToTable("vehicle_signature_imprints", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.AttachmentId).HasColumnName("attachment_id").IsRequired();
        builder.Property(x => x.ModuleCode).HasColumnName("module_code").HasMaxLength(40).IsRequired();
        builder.Property(x => x.PrivateKey).HasColumnName("private_key").IsRequired();
        builder.Property(x => x.PublicKey).HasColumnName("public_key").IsRequired();
        builder.Property(x => x.DocumentHash).HasColumnName("document_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Signature).HasColumnName("signature").IsRequired();
        builder.Property(x => x.SignedAt).HasColumnName("signed_at").IsRequired();
        builder.Property(x => x.WasSignedWithoutOwnerSignature)
            .HasColumnName("was_signed_without_owner_signature").IsRequired();
        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        builder.HasQueryFilter(x => x.DeletedAt == null);
    }
}
