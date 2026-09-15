using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.RevocationRequests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapea <c>tramites.revocation_request_email_dispatches</c> (HU #12579, Feature #12565).
/// DDL gestionado por SQL crudo (116): índice UNIQUE con <c>lower(recipient)</c>, CHECK y RLS no
/// los genera el scaffolding de EF. Excluida del ModelSnapshot — mismo patrón que
/// <c>PlateAssignmentEmailDispatchConfiguration</c>.
/// </summary>
internal sealed class RevocationRequestEmailDispatchConfiguration
    : IEntityTypeConfiguration<RevocationRequestEmailDispatch>
{
    public void Configure(EntityTypeBuilder<RevocationRequestEmailDispatch> builder)
    {
        builder.ToTable(
            "revocation_request_email_dispatches",
            SchemaNames.Tramites,
            t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.RevocationRequestId).HasColumnName("revocation_request_id").IsRequired();
        builder.Property(x => x.AttemptNumber).HasColumnName("attempt_number").IsRequired();
        builder.Property(x => x.Milestone).HasColumnName("milestone").HasMaxLength(20).IsRequired();

        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(320);
        builder.Property(x => x.RecipientName).HasColumnName("recipient_name").HasMaxLength(200);
        builder.Property(x => x.RecipientRole).HasColumnName("recipient_role").HasMaxLength(30).IsRequired();
        builder.Property(x => x.RecipientKind).HasColumnName("recipient_kind").HasMaxLength(30).IsRequired();
        builder.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(1000);
        builder.Property(x => x.DecisionReason).HasColumnName("decision_reason").HasMaxLength(500);
        builder.Property(x => x.Attempts).HasColumnName("attempts").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.QueuedAt).HasColumnName("queued_at").IsRequired();
        builder.Property(x => x.ProcessedAt).HasColumnName("processed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
    }
}
