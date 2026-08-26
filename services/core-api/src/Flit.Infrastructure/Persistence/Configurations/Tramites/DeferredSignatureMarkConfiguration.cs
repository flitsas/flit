using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// HU #11196 — marca de firma a posteriori. La tabla la crea el DDL embebido
/// <c>49-HU11196-firma-posterior.sql</c> (con su RLS y sus índices parciales), así que el modelo se
/// excluye de las migraciones: EF solo necesita saber leerla y escribirla.
/// </summary>
internal sealed class DeferredSignatureMarkConfiguration : IEntityTypeConfiguration<DeferredSignatureMark>
{
    public void Configure(EntityTypeBuilder<DeferredSignatureMark> builder)
    {
        builder.ToTable("deferred_signature_marks", SchemaNames.Tramites, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");
        builder.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(x => x.ProcedureInstanceId).HasColumnName("procedure_instance_id").IsRequired();
        builder.Property(x => x.PartyRole).HasColumnName("party_role").IsRequired();
        builder.Property(x => x.CompanyDocumentNumber).HasColumnName("company_document_number");
        builder.Property(x => x.RepresentativeDocumentType)
            .HasColumnName("representative_document_type").IsRequired();
        builder.Property(x => x.RepresentativeDocumentNumber)
            .HasColumnName("representative_document_number").IsRequired();
        builder.Property(x => x.Estado).HasColumnName("estado").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.AppliedAt).HasColumnName("applied_at");
        builder.Property(x => x.AppliedValidationId).HasColumnName("applied_validation_id");
        builder.Property(x => x.DiscardedReason).HasColumnName("discarded_reason");

        builder.Ignore(x => x.EstaPendiente);
    }
}
