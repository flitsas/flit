using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceSignatureConfiguration
    : IEntityTypeConfiguration<ProcedureInstanceSignature>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceSignature> builder)
    {
        builder.ToTable("procedure_instance_signatures", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_signatures_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Parte).HasColumnName("parte").HasMaxLength(20).IsRequired();
        builder.Property(x => x.DocTipo).HasColumnName("doc_tipo").HasMaxLength(40).IsRequired().HasDefaultValue("compraventa");
        builder.Property(x => x.Proveedor).HasColumnName("proveedor").HasMaxLength(40).IsRequired().HasDefaultValue("mock");
        builder.Property(x => x.Estado).HasColumnName("estado").HasMaxLength(20).IsRequired().HasDefaultValue("pendiente_envio");
        builder.Property(x => x.EnvelopeId).HasColumnName("envelope_id").HasMaxLength(200);
        builder.Property(x => x.PdfPath).HasColumnName("pdf_path").HasMaxLength(1000);
        builder.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64);
        builder.Property(x => x.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(x => x.SolicitadoAt).HasColumnName("solicitado_at").IsRequired();
        builder.Property(x => x.FirmadoAt).HasColumnName("firmado_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_procedure_instance_signatures_tenant_id_instance");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.Signatures)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_signatures_procedure_instances");
    }
}
