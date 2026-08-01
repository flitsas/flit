using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

internal sealed class TenantOperationalPolicyConfiguration
    : IEntityTypeConfiguration<TenantOperationalPolicy>
{
    public void Configure(EntityTypeBuilder<TenantOperationalPolicy> builder)
    {
        builder.ToTable("tenant_operational_policies", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => x.TenantId)
            .IsUnique()
            .HasDatabaseName("uq_tenant_operational_policies_tenant_id");

        builder.Property(x => x.AllowInitialRegistration).HasDefaultValue(false);
        builder.Property(x => x.AllowMiscNewVehicles).HasDefaultValue(true);
        builder.Property(x => x.OnlyOwnVehicles).HasDefaultValue(false);
        builder.Property(x => x.SignatureVaultEnabled).HasDefaultValue(false);
        builder.Property(x => x.PlatePreassignEnabled).HasDefaultValue(false);
        builder.Property(x => x.PlateFlowSkipToTerminado).HasDefaultValue(false);
        builder.Property(x => x.ValidateSoatWithRunt).HasDefaultValue(false);

        builder.Property(x => x.NotificationChannel)
            .HasMaxLength(20).HasDefaultValue("flit_smtp").IsRequired();
        builder.Property(x => x.NotificationTarget)
            .HasMaxLength(20).HasDefaultValue("submitter").IsRequired();

        builder.Property(x => x.PaymentMethods)
            .HasColumnType("jsonb").HasDefaultValueSql("'[]'").IsRequired();

        builder.Property(x => x.RuntProviderStrategy)
            .HasMaxLength(20).HasDefaultValue("verifik").IsRequired();
        builder.Property(x => x.RuntFailoverTimeoutMs).HasDefaultValue(60_000);

        builder.Property(x => x.ConsultationProviderConfig)
            .HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();

        builder.Property(x => x.AvaluoProviderConfig)
            .HasColumnType("jsonb").HasDefaultValueSql("'{}'").IsRequired();

        // FEATURE 02 — fuente de comparendos (internal | external).
        builder.Property(x => x.FinesQuerySource)
            .HasColumnType("text").HasDefaultValue("external").IsRequired();

        builder.Property(x => x.RowVersion).HasDefaultValue(0L).IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).IsRequired();
    }
}
