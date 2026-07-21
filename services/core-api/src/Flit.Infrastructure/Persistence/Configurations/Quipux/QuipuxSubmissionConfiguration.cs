using Flit.Infrastructure.Persistence.Schemas;
using Flit.Modules.Quipux.Domain.Envios;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Quipux;

/// <summary>Mapea <c>tramites.quipux_submissions</c> (radicación de un trámite en Quipux).</summary>
internal sealed class QuipuxSubmissionConfiguration : IEntityTypeConfiguration<QuipuxSubmission>
{
    public void Configure(EntityTypeBuilder<QuipuxSubmission> builder)
    {
        // DDL gestionado por migración SQL cruda (32-HU10710-quipux-integracion.sql), incluidos
        // el índice único parcial uq_quipux_submissions_activa y la política RLS tenant_isolation.
        builder.ToTable("quipux_submissions", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();

        builder.Property(x => x.DocumentName).HasColumnName("document_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.AttachmentId).HasColumnName("attachment_id").IsRequired();
        builder.Property(x => x.QuipuxProcedureType).HasColumnName("quipux_procedure_type").IsRequired();
        builder.Property(x => x.QuipuxRequirementType).HasColumnName("quipux_requirement_type").IsRequired();
        builder.Property(x => x.DivipoCode).HasColumnName("divipo_code").HasMaxLength(20).IsRequired();

        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.QxRegisterCode).HasColumnName("qx_register_code");
        builder.Property(x => x.QxProcedureCode).HasColumnName("qx_procedure_code");
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);

        builder.Property(x => x.Attempts).HasColumnName("attempts").IsRequired();
        builder.Property(x => x.ClaimedAt).HasColumnName("claimed_at");
        builder.Property(x => x.RegisteredAt).HasColumnName("registered_at");
        builder.Property(x => x.LastPolledAt).HasColumnName("last_polled_at");
        builder.Property(x => x.PollCount).HasColumnName("poll_count").IsRequired();
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Propiedades calculadas del dominio: no son columnas.
        builder.Ignore(x => x.EstaRegistrada);
        builder.Ignore(x => x.RequiereConsultaPrevia);

        builder.HasIndex(x => x.ProcedureInstanceId);
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}
