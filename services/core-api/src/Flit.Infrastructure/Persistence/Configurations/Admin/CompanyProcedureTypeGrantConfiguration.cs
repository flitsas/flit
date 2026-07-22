using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo EF de <see cref="CompanyProcedureTypeGrant"/> (FEATURE-08). La tabla la crea el DDL embebido
/// (39-F08-company-procedure-grants.sql) con su RLS y trigger de auditoría, por eso
/// <c>ExcludeFromMigrations</c>: EF solo la mapea para consultar/escribir, no la genera.
/// </summary>
internal sealed class CompanyProcedureTypeGrantConfiguration
    : IEntityTypeConfiguration<CompanyProcedureTypeGrant>
{
    public void Configure(EntityTypeBuilder<CompanyProcedureTypeGrant> builder)
    {
        builder.ToTable("company_procedure_type_grants", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.ProcedureTypeId).IsRequired();
        builder.Property(x => x.IsEnabled).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.ProcedureTypeId })
            .IsUnique()
            .HasDatabaseName("uq_company_procedure_type_grants");

        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_company_procedure_type_grants_tenant_id");
    }
}
