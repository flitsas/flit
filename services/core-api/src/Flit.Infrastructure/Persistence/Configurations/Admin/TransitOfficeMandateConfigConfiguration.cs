using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF Core de la configuración de mandato por OT (ADR-0036, HU #10912). El DDL lo gestiona la
/// migración SQL cruda (41-HU10912-transit-office-mandate-config.sql): UNIQUE por transit_office_id,
/// CHECK de template_code, FK al catálogo de OT y trigger de row_version que el MigrationBuilder no
/// modela. La entidad se excluye de migraciones (mismo patrón que <see cref="SignatureVaultEntityConfiguration"/>);
/// el snapshot la refleja para el modelo EF y se declara el trigger de row_version como concurrency token.
/// </summary>
internal sealed class TransitOfficeMandateConfigConfiguration : IEntityTypeConfiguration<TransitOfficeMandateConfigEntity>
{
    public void Configure(EntityTypeBuilder<TransitOfficeMandateConfigEntity> builder)
    {
        builder.ToTable("transit_office_mandate_config", SchemaNames.Admin, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_transit_office_mandate_config_row_version");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TransitOfficeId).IsRequired();
        builder.Property(x => x.TemplateCode).HasMaxLength(30).IsRequired();
        builder.Property(x => x.RequiresForNaturalPerson).IsRequired();
        builder.Property(x => x.InstitutionalMandataryName).HasMaxLength(200);
        builder.Property(x => x.InstitutionalMandataryNit).HasMaxLength(20);
        // HU #11204 — familia del mandatario + datos propios del OT (antes incrustados en el generador).
        builder.Property(x => x.MandataryFamily).HasMaxLength(30).IsRequired().HasDefaultValue("individuo");
        builder.Property(x => x.ChamberCity).HasMaxLength(120);
        builder.Property(x => x.MandatarySigla).HasMaxLength(60);
        builder.Property(x => x.AssignmentMode).HasMaxLength(20).IsRequired().HasDefaultValue("signer");
        builder.Property(x => x.CustomTemplateKind).HasMaxLength(20).IsRequired().HasDefaultValue("none");
        builder.Property(x => x.CustomTemplateStoragePath).HasMaxLength(1000);
        builder.Property(x => x.CustomTemplateSha256).HasMaxLength(64);
        builder.Property(x => x.CustomTemplateFileName).HasMaxLength(260);
        builder.Property(x => x.CustomTemplateBody);
        builder.Property(x => x.CustomFieldManifest).HasColumnType("jsonb");
        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => x.TransitOfficeId)
            .IsUnique()
            .HasDatabaseName("uq_transit_office_mandate_config_transit_office");
    }
}
