using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Infrastructure.Persistence.Schemas;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Infrastructure.Persistence.Configurations.Admin;

/// <summary>
/// Mapeo de <c>admin.impronta_generations</c> (HU #10466 / ADR-0022). FKs a
/// <c>identity.tenants</c>/<c>identity.users</c>, trigger de auditoría (sin <c>row_version</c>) y
/// política RLS se materializan vía SQL crudo en la migración (mismo patrón que el resto del schema
/// <c>admin</c> — ver <c>TransitOfficeProfileConfiguration</c>).
///
/// El CHECK <c>ck_impronta_generations_identificador_vehiculo</c> (al menos un identificador de
/// vehículo obligatorio) se eliminó en la migración <c>DropImprontaVehiculoIdentificadorCheck</c>:
/// verificado contra el proveedor real (Kyverum RUNT) que la impronta se genera correctamente sin
/// numMotor/numChasis/numSerie, resolviendo el vehículo vía placa+documento.
/// </summary>
internal sealed class ImprontaGenerationEntityConfiguration : IEntityTypeConfiguration<ImprontaGenerationEntity>
{
    public void Configure(EntityTypeBuilder<ImprontaGenerationEntity> builder)
    {
        builder.ToTable("impronta_generations", SchemaNames.Admin);

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasDefaultValueSql("uuidv7()");

        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => x.TenantId).HasDatabaseName("ix_impronta_generations_tenant_id");

        builder.Property(x => x.FlitUserId).IsRequired();
        builder.HasIndex(x => x.FlitUserId).HasDatabaseName("ix_impronta_generations_flit_user_id");

        builder.Property(x => x.Radicado).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.Radicado)
            .IsUnique()
            .HasDatabaseName("uq_impronta_generations_radicado");

        builder.Property(x => x.HashSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FechaImpresa).IsRequired();

        builder.Property(x => x.Placa).HasMaxLength(10).IsRequired();
        builder.HasIndex(x => x.Placa).HasDatabaseName("ix_impronta_generations_placa");

        builder.Property(x => x.NumMotor).HasMaxLength(50);
        builder.Property(x => x.NumChasis).HasMaxLength(50);
        builder.Property(x => x.NumSerie).HasMaxLength(50);
        builder.Property(x => x.Marca).HasMaxLength(100);
        builder.Property(x => x.Linea).HasMaxLength(100);
        builder.Property(x => x.Modelo).HasMaxLength(10);

        builder.Property(x => x.OrgNombre).HasMaxLength(200).IsRequired();
        builder.Property(x => x.OrgNit).HasMaxLength(20).IsRequired();
        builder.Property(x => x.OrgCiudad).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Operador).HasMaxLength(200).IsRequired();

        builder.Property(x => x.PdfContent).IsRequired();
        builder.Property(x => x.PdfSizeBytes).IsRequired();

        builder.Property(x => x.CreatedAt).HasDefaultValueSql("now()").IsRequired();

        // ix_impronta_generations_created_at (DESC) se crea vía SQL crudo en la migración: el
        // scaffolding de EF Core para HasIndex(...).IsDescending() en una sola columna genera
        // `descending: new bool[0]` (CA1825 + semántica ambigua) en vez de `new[] { true }`.
    }
}
