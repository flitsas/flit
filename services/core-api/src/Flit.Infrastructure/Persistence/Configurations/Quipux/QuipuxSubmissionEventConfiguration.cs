using Flit.Infrastructure.Persistence.Schemas;
using Flit.Modules.Quipux.Domain.Trazabilidad;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Quipux;

/// <summary>Mapea <c>tramites.quipux_submission_events</c> (bitácora por radicación).</summary>
internal sealed class QuipuxSubmissionEventConfiguration : IEntityTypeConfiguration<QuipuxSubmissionEvent>
{
    public void Configure(EntityTypeBuilder<QuipuxSubmissionEvent> builder)
    {
        // DDL gestionado por migración SQL cruda (32-HU10710-quipux-integracion.sql).
        builder.ToTable("quipux_submission_events", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id").IsRequired();
        builder.Property(x => x.Stage).HasColumnName("stage").HasMaxLength(40).IsRequired();
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(20).IsRequired();

        // jsonb: el escritor envuelve los strings como escalar JSON — un string plano no es JSON
        // válido y Postgres lo rechaza con 22P02.
        builder.Property(x => x.Detail).HasColumnName("detail").HasColumnType("jsonb");

        builder.Property(x => x.CorrelationId).HasColumnName("correlation_id");
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("now()");

        builder.HasIndex(x => new { x.SubmissionId, x.OccurredAt });
        builder.HasIndex(x => new { x.TenantId, x.OccurredAt });
    }
}
