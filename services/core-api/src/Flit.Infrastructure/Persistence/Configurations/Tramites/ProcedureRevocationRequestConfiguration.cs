using Flit.Tramites.Domain.RevocationRequests;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #12571 (Feature #12565) — mapeo EF de <c>tramites.procedure_revocation_requests</c>. DDL gestionado
/// por migración SQL cruda (HU #12570, <c>115-HU12570-procedure-revocation-requests.sql</c>): CHECKs,
/// índice único parcial de activas, RLS y triggers los fija ese script, no EF (mismo patrón que
/// <c>ProcedureInstancePrendaConfiguration</c>).
/// </summary>
internal sealed class ProcedureRevocationRequestConfiguration : IEntityTypeConfiguration<ProcedureRevocationRequest>
{
    public void Configure(EntityTypeBuilder<ProcedureRevocationRequest> builder)
    {
        builder.ToTable("procedure_revocation_requests", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_procedure_revocation_requests_row_version");
            t.HasTrigger("tr_procedure_revocation_requests_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.AttemptNumber).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired()
            .HasDefaultValue(ProcedureRevocationRequestStatus.Solicitada);
        builder.Property(x => x.Reason).HasMaxLength(500);
        builder.Property(x => x.RequestedAt).IsRequired();
        builder.Property(x => x.DecisionReason).HasMaxLength(500);
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();

        // Consumo principal: historial de intentos, más recientes primero (AC5).
        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId, x.RequestedAt })
            .HasDatabaseName("ix_procedure_revocation_requests_tenant_instance");

        // Documental: unicidad real (procedure_instance_id, attempt_number) la exige el CHECK/UNIQUE del
        // DDL crudo; se declara aquí para que el modelo EF la conozca (no emite DDL, tabla excluida).
        builder.HasIndex(x => new { x.ProcedureInstanceId, x.AttemptNumber })
            .IsUnique()
            .HasDatabaseName("uq_procedure_revocation_requests_instance_attempt");

        // AC4 — a lo sumo una fila activa (solicitada|en_revision) por trámite. El índice único parcial
        // real vive en el DDL crudo (uq_procedure_revocation_requests_active_per_instance); aquí solo se
        // documenta la intención en el modelo EF, que no emite DDL para esta tabla.
        builder.HasIndex(x => new { x.TenantId, x.ProcedureInstanceId })
            .HasFilter("status IN ('solicitada', 'en_revision')")
            .IsUnique()
            .HasDatabaseName("uq_procedure_revocation_requests_active_per_instance");
    }
}
