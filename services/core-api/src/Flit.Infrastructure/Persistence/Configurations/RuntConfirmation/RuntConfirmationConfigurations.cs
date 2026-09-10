using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.RuntConfirmation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.RuntConfirmation;

/// <summary>
/// Mapeos EF de Confirmación RUNT (Feature #12276). El DDL lo gestiona la migración SQL cruda
/// (<c>107-F12276-confirmacion-runt.sql</c>): CHECKs de vocabulario, índice único de fila única, RLS
/// y triggers que el <c>MigrationBuilder</c> no modela. Las tres tablas se excluyen de migraciones.
/// </summary>
internal sealed class RuntConfirmationSettingsConfiguration : IEntityTypeConfiguration<RuntConfirmationSettings>
{
    public void Configure(EntityTypeBuilder<RuntConfirmationSettings> builder)
    {
        builder.ToTable("runt_confirmation_settings", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_runt_confirmation_settings_row_version");
            t.HasTrigger("tr_runt_confirmation_settings_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();
        builder.Property(x => x.RunAtLocal).HasColumnName("run_at_local").HasMaxLength(5).IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(40).IsRequired();
        builder.Property(x => x.GraceDays).HasColumnName("grace_days").IsRequired();
        builder.Property(x => x.DiscrepancyAfterRuns).HasColumnName("discrepancy_after_runs").IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").IsRequired();

        builder.Property(x => x.RowVersion).HasColumnName("row_version").HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
    }
}

internal sealed class RuntConfirmationRunConfiguration : IEntityTypeConfiguration<RuntConfirmationRun>
{
    public void Configure(EntityTypeBuilder<RuntConfirmationRun> builder)
    {
        builder.ToTable("runt_confirmation_runs", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Trigger).HasColumnName("trigger").HasMaxLength(20).IsRequired();
        builder.Property(x => x.StartedAt).HasColumnName("started_at").IsRequired();
        builder.Property(x => x.FinishedAt).HasColumnName("finished_at");
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(40);
        builder.Property(x => x.SkippedReason).HasColumnName("skipped_reason").HasMaxLength(40);

        builder.Property(x => x.Consulted).HasColumnName("consulted").IsRequired();
        builder.Property(x => x.Confirmed).HasColumnName("confirmed").IsRequired();
        builder.Property(x => x.Pending).HasColumnName("pending").IsRequired();
        builder.Property(x => x.Discrepancies).HasColumnName("discrepancies").IsRequired();
        builder.Property(x => x.Unverifiable).HasColumnName("unverifiable").IsRequired();
        builder.Property(x => x.Errors).HasColumnName("errors").IsRequired();
        builder.Property(x => x.ProviderCalls).HasColumnName("provider_calls").IsRequired();
        builder.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);

        builder.HasIndex(x => x.StartedAt).HasDatabaseName("ix_runt_confirmation_runs_started");
    }
}

internal sealed class RuntConfirmationAttemptConfiguration : IEntityTypeConfiguration<RuntConfirmationAttempt>
{
    public void Configure(EntityTypeBuilder<RuntConfirmationAttempt> builder)
    {
        builder.ToTable("runt_confirmation_attempts", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.RunId).HasColumnName("run_id");

        builder.Property(x => x.AttemptNo).HasColumnName("attempt_no").IsRequired();
        builder.Property(x => x.QueriedAt).HasColumnName("queried_at").IsRequired();
        builder.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(40).IsRequired();
        builder.Property(x => x.QueryKind).HasColumnName("query_kind").HasMaxLength(20).IsRequired();

        builder.Property(x => x.Verdict).HasColumnName("verdict").HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReasonText).HasColumnName("reason_text").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.RuleVersion).HasColumnName("rule_version").HasMaxLength(40).IsRequired();

        builder.Property(x => x.RawPayloadId).HasColumnName("raw_payload_id");
        builder.Property(x => x.SellerRawPayloadId).HasColumnName("seller_raw_payload_id");
        builder.Property(x => x.RequestedBy).HasColumnName("requested_by");
        builder.Property(x => x.ReevaluatedFromAttemptId).HasColumnName("reevaluated_from_attempt_id");
        builder.Property(x => x.FlagApplied).HasColumnName("flag_applied").HasMaxLength(20);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => new { x.ProcedureInstanceId, x.QueriedAt }).HasDatabaseName("ix_runt_confirmation_attempts_instance");
        builder.HasIndex(x => x.RunId).HasDatabaseName("ix_runt_confirmation_attempts_run");
        builder.HasIndex(x => x.QueriedAt).HasDatabaseName("ix_runt_confirmation_attempts_queried");
        builder.HasIndex(x => new { x.TenantId, x.Verdict, x.QueriedAt }).HasDatabaseName("ix_runt_confirmation_attempts_tenant_verdict");
    }
}
