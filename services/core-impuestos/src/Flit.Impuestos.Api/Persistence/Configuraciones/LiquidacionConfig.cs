using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Flit.Impuestos.Api.Persistence;

/// <summary>Tipo de liquidación: la individual es un lote de un ítem (supuesto S-A).</summary>
public enum TipoLiquidacion
{
    Individual,
    Masiva,
}

/// <summary>
/// Cabecera de una liquidación (lote) con sus contadores por categoría y la copia de la configuración vigente al crearla (F3-CF3).
/// Concurrencia optimista con la columna de sistema <c>xmin</c>.
/// </summary>
public sealed class Liquidacion : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    public Guid TenantId { get; set; }

    public TipoLiquidacion Tipo { get; set; }

    public EstadoLote Estado { get; set; } = EstadoLote.Creado;

    public string? NombreArchivo { get; set; }

    public int TotalItems { get; set; }

    public int CExitosas { get; set; }

    public int CConDeuda { get; set; }

    public int CPazYSalvo { get; set; }

    public int CError { get; set; }

    public int CDatosIncompletos { get; set; }

    public int CDptoNoHabilitado { get; set; }

    public int CIncierto { get; set; }

    public int CSinResultado { get; set; }

    public int CPendientes { get; set; }

    /// <summary>jsonb <c>{maxRegistros, departamentosHabilitados, concurrencia}</c> vigente al crear la liquidación.</summary>
    public string ConfigCopia { get; set; } = "{}";

    public Guid CreadaPor { get; set; }

    public DateTimeOffset CreadaEn { get; set; }

    public DateTimeOffset? TerminadaEn { get; set; }

    public Guid? CerradaPor { get; set; }

    public DateTimeOffset? CerradaEn { get; set; }

    public DateTimeOffset? AvisoEnviadoEn { get; set; }

    /// <summary>Token de concurrencia (columna de sistema <c>xmin</c>).</summary>
    public uint Version { get; set; }
}

/// <summary><c>impuestos.liquidaciones</c> (plan §4.1).</summary>
internal sealed class LiquidacionConfig : IEntityTypeConfiguration<Liquidacion>
{
    public void Configure(EntityTypeBuilder<Liquidacion> builder)
    {
        builder.ToTable("liquidaciones", t =>
        {
            t.HasComment("Liquidaciones (lotes) de la compañía; la individual es un lote de un ítem (S-A).");
            t.HasCheckConstraint("ck_liquidaciones_tipo", $"tipo IN ({CodigosDeEnum.ListaSql<TipoLiquidacion>()})");
            t.HasCheckConstraint("ck_liquidaciones_estado", $"estado IN ({CodigosDeEnum.ListaSql<EstadoLote>()})");
        });
        builder.HasKey(x => x.Id).HasName("pk_liquidaciones");
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Tipo).HasConversion(CodigosDeEnum.Convertidor<TipoLiquidacion>()).HasMaxLength(12);
        builder.Property(x => x.Estado).HasConversion(CodigosDeEnum.Convertidor<EstadoLote>()).HasMaxLength(28);
        builder.Property(x => x.NombreArchivo).HasMaxLength(200);
        foreach (var contador in new[]
                 {
                     nameof(Liquidacion.CExitosas), nameof(Liquidacion.CConDeuda), nameof(Liquidacion.CPazYSalvo), nameof(Liquidacion.CError),
                     nameof(Liquidacion.CDatosIncompletos), nameof(Liquidacion.CDptoNoHabilitado), nameof(Liquidacion.CIncierto),
                     nameof(Liquidacion.CSinResultado),
                 })
        {
            builder.Property<int>(contador).HasDefaultValue(0).ValueGeneratedNever();
        }

        builder.Property(x => x.ConfigCopia).HasColumnType("jsonb").HasComment("{maxRegistros, departamentosHabilitados, concurrencia} al crear (F3-CF3).");
        builder.Property(x => x.Version).IsRowVersion();
        builder.HasIndex(x => new { x.TenantId, x.CreadaEn }).IsDescending(false, true).HasDatabaseName("ix_liq_tenant_creada");
    }
}
