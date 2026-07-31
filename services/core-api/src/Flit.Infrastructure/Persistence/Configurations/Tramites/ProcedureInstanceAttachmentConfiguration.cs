using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class ProcedureInstanceAttachmentConfiguration : IEntityTypeConfiguration<ProcedureInstanceAttachment>
{
    public void Configure(EntityTypeBuilder<ProcedureInstanceAttachment> builder)
    {
        builder.ToTable("procedure_instance_attachments", SchemaNames.Tramites, t =>
            t.HasTrigger("tr_procedure_instance_attachments_audit"));

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Tipo).HasColumnName("tipo").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Filename).HasColumnName("filename").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Mimetype).HasColumnName("mimetype").HasMaxLength(150).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes").IsRequired();
        builder.Property(x => x.Sha256).HasColumnName("sha256").HasMaxLength(64).IsRequired();
        builder.Property(x => x.StoragePath).HasColumnName("storage_path").HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Source).HasColumnName("source").HasMaxLength(20).IsRequired().HasDefaultValue("user");
        builder.Property(x => x.UploadedAt).HasColumnName("uploaded_at").IsRequired();
        builder.Property(x => x.UploadedBy).HasColumnName("uploaded_by");
        // HU #10936 — escritura utilizada (admin.company_deeds.id) para las escrituras de sistema.
        builder.Property(x => x.SourceDeedId).HasColumnName("source_deed_id");

        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasDatabaseName("ix_procedure_instance_attachments_tenant_id_instance");

        builder.HasOne(x => x.ProcedureInstance)
            .WithMany(x => x.Attachments)
            .HasForeignKey(x => x.ProcedureInstanceId)
            .OnDelete(DeleteBehavior.Cascade)
            .HasConstraintName("fk_procedure_instance_attachments_procedure_instances");
    }
}
