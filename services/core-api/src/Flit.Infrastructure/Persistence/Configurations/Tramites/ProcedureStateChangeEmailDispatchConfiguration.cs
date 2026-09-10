using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapea <c>tramites.procedure_state_change_email_dispatches</c> (HU #11461).
/// DDL gestionado por SQL crudo (69): índices UNIQUE con <c>lower(recipient)</c>, CHECK y RLS
/// no los genera el scaffolding de EF. Excluida del ModelSnapshot.
/// </summary>
internal sealed class ProcedureStateChangeEmailDispatchConfiguration
    : IEntityTypeConfiguration<ProcedureStateChangeEmailDispatch>
{
    public void Configure(EntityTypeBuilder<ProcedureStateChangeEmailDispatch> builder)
    {
        builder.ToTable(
            "procedure_state_change_email_dispatches",
            SchemaNames.Tramites,
            t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.OutboxId).HasColumnName("outbox_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();

        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(320);
        builder.Property(x => x.RecipientName).HasColumnName("recipient_name").HasMaxLength(200);
        builder.Property(x => x.RecipientRole).HasColumnName("recipient_role").HasMaxLength(30).IsRequired();
        builder.Property(x => x.RecipientKind).HasColumnName("recipient_kind").HasMaxLength(30).IsRequired();
        builder.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(1000);
        builder.Property(x => x.Attempts).HasColumnName("attempts").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.QueuedAt).HasColumnName("queued_at").IsRequired();
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
    }
}
