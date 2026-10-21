using Flit.Diagnostico.Api.Modulos.Configuracion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Diagnostico.Api.Persistence.Configuraciones;

/// <summary>
/// <c>diagnostico.cambios_contrato</c> (HU #13736): auditoría de solo inserción de los cambios del nivel contractual de
/// <c>configuracion_empresa</c>. Como <c>lecturas_backoffice</c>, no lleva columnas de edición, borrado lógico ni
/// <c>row_version</c> (ADR-0072). La FK a la configuración de la misma empresa impide auditar una configuración inexistente.
/// </summary>
internal sealed class CambioContratoConfig : IEntityTypeConfiguration<CambioContrato>
{
    public void Configure(EntityTypeBuilder<CambioContrato> builder)
    {
        builder.ToTable("cambios_contrato", t => t.HasComment(
            "Cambios del nivel contractual de la configuración por compañía (HU #13736). Solo inserción."));
        builder.HasKey(x => x.Id).HasName("pk_cambios_contrato");
        builder.Property(x => x.Id).ValueGeneratedNever().HasComment("uuid v7 generado por la aplicación.");
        builder.Property(x => x.TenantId).HasComment("Empresa cuya configuración cambió.");
        builder.Property(x => x.UsuarioId).HasComment("@pii:low Usuario del backoffice que hizo el cambio (claim sub, seudónimo).");
        builder.Property(x => x.Campo).HasMaxLength(64).HasComment("Columna contractual cambiada (p. ej. fuentes_habilitadas).");
        builder.Property(x => x.ValorAnterior).HasColumnType("jsonb").HasComment("Valor anterior; null si no había.");
        builder.Property(x => x.ValorNuevo).HasColumnType("jsonb").HasComment("Valor nuevo.");
        builder.Property(x => x.Fecha).HasDefaultValueSql("now()").HasComment("Momento del cambio.");

        builder.HasOne<ConfiguracionEmpresa>().WithMany()
            .HasForeignKey(x => x.TenantId)
            .HasConstraintName("fk_cambios_contrato_configuracion_empresa")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.TenantId, x.Fecha }).HasDatabaseName("ix_cambios_contrato_tenant_fecha");
    }
}
