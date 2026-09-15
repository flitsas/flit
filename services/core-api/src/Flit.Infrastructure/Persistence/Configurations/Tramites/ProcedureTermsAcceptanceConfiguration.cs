using Flit.Infrastructure.Persistence.Schemas;
using Flit.Tramites.Domain.TermsAcceptance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Tramites;

/// <summary>
/// Mapeo EF de la aceptación de T&amp;C (Epic #12543). El DDL lo gestiona la migración SQL cruda
/// (<c>113-E12543-procedure-terms-acceptances.sql</c>): RLS y trigger de auditoría que el
/// <c>MigrationBuilder</c> no modela, así que la tabla se excluye de migraciones.
/// </summary>
internal sealed class ProcedureTermsAcceptanceConfiguration : IEntityTypeConfiguration<ProcedureTermsAcceptance>
{
    public void Configure(EntityTypeBuilder<ProcedureTermsAcceptance> builder)
    {
        builder.ToTable("procedure_terms_acceptances", SchemaNames.Tramites, t =>
        {
            t.ExcludeFromMigrations();
            t.HasTrigger("tr_procedure_terms_acceptances_audit");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).HasColumnName("tenant_id");
        builder.Property(x => x.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(x => x.ProcedureTypeCode).HasColumnName("procedure_type_code").HasMaxLength(50).IsRequired();
        builder.Property(x => x.TermsUrl).HasColumnName("terms_url").IsRequired();
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at").IsRequired();
        builder.Property(x => x.ClientIp).HasColumnName("client_ip").HasMaxLength(64);
        builder.Property(x => x.UserAgent).HasColumnName("user_agent");

        builder.HasIndex(x => new { x.UserId, x.AcceptedAt }).HasDatabaseName("ix_procedure_terms_acceptances_user_accepted");
        builder.HasIndex(x => new { x.TenantId, x.AcceptedAt }).HasDatabaseName("ix_procedure_terms_acceptances_tenant_accepted");
    }
}
