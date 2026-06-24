using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

internal sealed class IdentityValidationOutboxConfiguration
    : IEntityTypeConfiguration<IdentityValidationOutbox>
{
    public void Configure(EntityTypeBuilder<IdentityValidationOutbox> builder)
    {
        builder.ToTable("identity_validation_outbox", SchemaNames.Tramites);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ValidationId).HasColumnName("validation_id").IsRequired();
        builder.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.Property(x => x.Attempts).HasColumnName("attempts").IsRequired().HasDefaultValue(0);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(x => x.ValidationId)
            .HasDatabaseName("ix_identity_validation_outbox_validation");

        // Índice único parcial (AC8): a lo sumo un evento 'completed' por validación ⇒ idempotencia
        // del webhook garantizada a nivel BD.
        builder.HasIndex(x => x.ValidationId)
            .IsUnique()
            .HasDatabaseName("uq_identity_validation_outbox_completed")
            .HasFilter("event_type = 'identity_validation.completed'");

        // Cola de pendientes (published_at IS NULL) para el worker de fase 2.
        builder.HasIndex(x => x.OccurredAt)
            .HasDatabaseName("ix_identity_validation_outbox_unpublished")
            .HasFilter("published_at IS NULL");
    }
}
