using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Configuración EF Core para <see cref="TenantRuntPersonOverrideEntity"/>
/// (HU #14642 — admin.tenant_runt_person_overrides).
/// </summary>
internal sealed class TenantRuntPersonOverrideConfiguration
    : IEntityTypeConfiguration<TenantRuntPersonOverrideEntity>
{
    public void Configure(EntityTypeBuilder<TenantRuntPersonOverrideEntity> builder)
    {
        builder.ToTable("tenant_runt_person_overrides", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.Property(x => x.DocumentType).HasMaxLength(10).IsRequired();
        builder.Property(x => x.DocumentNumber).IsRequired();

        // Nombre desglosado
        builder.Property(x => x.FirstName).IsRequired();
        builder.Property(x => x.SecondName);
        builder.Property(x => x.FirstLastName).IsRequired();
        builder.Property(x => x.SecondLastName);

        // Estado
        builder.Property(x => x.CitizenStatus).HasMaxLength(50).IsRequired().HasDefaultValue("ACTIVA");
        builder.Property(x => x.DriverStatus).HasMaxLength(50).IsRequired().HasDefaultValue("ACTIVO");

        // Licencia
        builder.Property(x => x.HasActiveLicense).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.LicenseCategories);

        // Multas
        builder.Property(x => x.HasPendingFines).IsRequired().HasDefaultValue(false);
        builder.Property(x => x.NroPazYSalvo);

        // Control
        builder.Property(x => x.Reason).IsRequired();
        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(x => x.ExpiresAt);

        // Auditoría
        builder.Property(x => x.CreatedBy);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.UpdatedBy);
        builder.Property(x => x.UpdatedAt);

        // Índice de lookup por tenant (para listar overrides de una empresa).
        builder.HasIndex(x => x.TenantId)
            .HasDatabaseName("ix_tenant_runt_person_overrides_tenant_id");

        // Nota: el índice parcial único uq_tenant_runt_person_overrides_active y el índice de lookup
        // con funciones (upper/lower) solo viven en el DDL 203 — EF no admite funciones en índices
        // de forma portable, y el DDL es la fuente canónica de esos índices.
    }
}
