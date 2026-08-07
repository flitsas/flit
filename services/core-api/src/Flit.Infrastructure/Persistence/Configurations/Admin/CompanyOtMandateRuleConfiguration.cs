using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class CompanyOtMandateRuleConfiguration
    : IEntityTypeConfiguration<CompanyOtMandateRuleEntity>
{
    public void Configure(EntityTypeBuilder<CompanyOtMandateRuleEntity> builder)
    {
        builder.ToTable(
            "company_ot_mandate_rules", SchemaNames.Admin, t => t.ExcludeFromMigrations());

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.CompanyTenantId).HasColumnName("company_tenant_id").IsRequired();
        builder.Property(x => x.TransitOfficeId).HasColumnName("transit_office_id").IsRequired();
        builder.Property(x => x.AssignmentMode)
            .HasColumnName("assignment_mode")
            .HasMaxLength(20)
            .IsRequired()
            .HasDefaultValue("signer");
        builder.Property(x => x.MandataryFamily)
            .HasColumnName("mandatary_family")
            .HasMaxLength(40)
            .IsRequired()
            .HasDefaultValue("individuo");
        builder.Property(x => x.InstitutionalMandataryName)
            .HasColumnName("institutional_mandatary_name")
            .HasMaxLength(300);
        builder.Property(x => x.InstitutionalMandataryNit)
            .HasColumnName("institutional_mandatary_nit")
            .HasMaxLength(30);
        builder.Property(x => x.ChamberCity).HasColumnName("chamber_city").HasMaxLength(120);
        builder.Property(x => x.MandatarySigla).HasColumnName("mandatary_sigla").HasMaxLength(40);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.CreatedBy).HasColumnName("created_by");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.Property(x => x.UpdatedBy).HasColumnName("updated_by");

        builder.HasIndex(x => new { x.CompanyTenantId, x.TransitOfficeId })
            .IsUnique()
            .HasDatabaseName("uq_company_ot_mandate_rules");
    }
}
