using Flit.Infrastructure.Persistence.Entities.Quipux;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Quipux;

/// <summary>Mapea <c>admin.quipux_settings</c> (fila única, secretos cifrados en columnas <c>*_enc</c>).</summary>
internal sealed class QuipuxSettingsConfiguration : IEntityTypeConfiguration<QuipuxSettingsRow>
{
    public void Configure(EntityTypeBuilder<QuipuxSettingsRow> builder)
    {
        // DDL gestionado por migración SQL cruda (32-HU10710-quipux-integracion.sql); la entidad
        // vive en el modelo EF solo para consultas y se excluye del snapshot/migraciones EF.
        builder.ToTable("quipux_settings", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.Enabled).HasColumnName("enabled").IsRequired();

        builder.Property(x => x.UrlLogin).HasColumnName("url_login").HasMaxLength(500).IsRequired();
        builder.Property(x => x.UrlRegisterDocument).HasColumnName("url_register_document").HasMaxLength(500).IsRequired();
        builder.Property(x => x.UrlValidateStatus).HasColumnName("url_validate_status").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Username).HasColumnName("username").HasMaxLength(120).IsRequired();
        builder.Property(x => x.PasswordEnc).HasColumnName("password_enc").IsRequired();
        builder.Property(x => x.ConsumerCode).HasColumnName("consumer_code").HasMaxLength(40).IsRequired();

        builder.Property(x => x.Bucket).HasColumnName("bucket").HasMaxLength(200).IsRequired();
        builder.Property(x => x.S3Prefix).HasColumnName("s3_prefix").HasMaxLength(100).IsRequired();
        builder.Property(x => x.AwsRegion).HasColumnName("aws_region").HasMaxLength(40).IsRequired();
        builder.Property(x => x.AwsAccessKeyId).HasColumnName("aws_access_key_id").HasMaxLength(200).IsRequired();
        builder.Property(x => x.AwsSecretAccessKeyEnc).HasColumnName("aws_secret_access_key_enc").IsRequired();

        builder.Property(x => x.OfficerDocumentType).HasColumnName("officer_document_type").HasColumnType("smallint").IsRequired();
        builder.Property(x => x.OfficerDocumentNumber).HasColumnName("officer_document_number").HasMaxLength(40).IsRequired();

        builder.Property(x => x.RegisterIntervalMinutes).HasColumnName("register_interval_minutes").IsRequired();
        builder.Property(x => x.PollIntervalMinutes).HasColumnName("poll_interval_minutes").IsRequired();
        builder.Property(x => x.BatchSize).HasColumnName("batch_size").IsRequired();
        builder.Property(x => x.MaxAttempts).HasColumnName("max_attempts").IsRequired();
        builder.Property(x => x.MaxPolls).HasColumnName("max_polls").IsRequired();
        builder.Property(x => x.TimeoutSeconds).HasColumnName("timeout_seconds").IsRequired();

        builder.Property(x => x.RowVersion).HasColumnName("row_version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
    }
}
