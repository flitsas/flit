using Flit.Infrastructure.Persistence.Entities.Integrations;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Integrations;

/// <summary>HU #13084 — mapeo de <c>integrations.external_clients</c>; el esquema lo crea el DDL 125.</summary>
internal sealed class ExternalClientConfiguration : IEntityTypeConfiguration<ExternalClient>
{
    public void Configure(EntityTypeBuilder<ExternalClient> builder)
    {
        builder.ToTable("external_clients", SchemaNames.Integrations, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_external_clients_row_version");
            t.HasTrigger("tr_external_clients_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.ClientId).HasColumnName("client_id").HasMaxLength(64).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(300).IsRequired();
        builder.Property(x => x.SecretHash).HasColumnName("secret_hash").IsRequired();
        builder.Property(x => x.PreviousSecretHash).HasColumnName("previous_secret_hash");
        builder.Property(x => x.SecretRotatedAt).HasColumnName("secret_rotated_at");
        builder.Property(x => x.MustRotate).HasColumnName("must_rotate").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.Scopes).HasColumnName("scopes").HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.FailedAttempts).HasColumnName("failed_attempts").HasDefaultValue(0).IsRequired();
        builder.Property(x => x.LockedUntil).HasColumnName("locked_until");
        builder.Property(x => x.LastTokenAt).HasColumnName("last_token_at");

        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        // Refleja la restricción única del DDL; no la crea (la entidad está ExcludeFromMigrations).
        builder.HasIndex(x => x.ClientId).IsUnique().HasDatabaseName("uq_external_clients_client_id");
    }
}
