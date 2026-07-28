using Flit.Tramites.Domain.Entities;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF Core de <see cref="Person"/> — HU #10865 (Feature #10864, CF-00, ADR-0030).
/// La tabla <c>tramites.persons</c> la crea la migración DDL embebida
/// <c>41-HU10865-persons.sql</c> (RLS, triggers, índices, comentarios PII).
/// Mismo patrón de exclusión que <see cref="PersonDataConsentConfiguration"/>.
/// </summary>
internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.ToTable("persons", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_persons_row_version");
            t.HasTrigger("tr_persons_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.DocumentType)
            .HasColumnName("document_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.DocumentNumber)
            .HasColumnName("document_number")
            .HasMaxLength(40)
            .IsRequired();

        builder.Property(x => x.FullName)
            .HasColumnName("full_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Email)
            .HasColumnName("email")
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.PersonType)
            .HasColumnName("person_type")
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue(PersonTypes.Natural);

        // ── Representante legal embebido ──────────────────────────────────────────
        builder.Property(x => x.LegalRepDocumentType)
            .HasColumnName("legal_rep_document_type")
            .HasMaxLength(20);

        builder.Property(x => x.LegalRepDocumentNumber)
            .HasColumnName("legal_rep_document_number")
            .HasMaxLength(40);

        builder.Property(x => x.LegalRepName)
            .HasColumnName("legal_rep_name")
            .HasMaxLength(200);

        builder.Property(x => x.LegalRepEmail)
            .HasColumnName("legal_rep_email")
            .HasMaxLength(320);

        // ── Columnas estándar (checklist §A5, §B8) ───────────────────────────────
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");
        builder.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        builder.Property(x => x.DeletedBy).HasColumnName("deleted_by");

        builder.Property(x => x.RowVersion)
            .HasColumnName("row_version")
            .HasDefaultValue(1)
            .IsConcurrencyToken();

        // ── Índices (checklist §A9, §A11, §A12) ─────────────────────────────────
        // Índice FK a tenant (checklist §A9: FK siempre con índice, tenant_id primero §A11)
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_persons_tenant_id");

        // Clave de negocio única por tenant (parcial sobre no-eliminados)
        builder.HasIndex(x => new { x.TenantId, x.DocumentType, x.DocumentNumber })
            .IsUnique()
            .HasDatabaseName("uq_persons_tenant_document")
            .HasFilter("deleted_at IS NULL");
    }
}
