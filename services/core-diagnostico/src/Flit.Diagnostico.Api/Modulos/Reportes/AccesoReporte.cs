using Flit.Diagnostico.Api.Modulos.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Flit.Diagnostico.Api.Modulos.Reportes;

/// <summary>
/// Una visualización o descarga del módulo de reportes (tabla <c>diagnostico.accesos_reporte</c>, HU #14579, BE-0, SP-8, ADR-0088; Ley 1581
/// art. 17 lit. d). Solo se inserta: <see cref="ValidarSoloInsercion"/> hace fallar todo guardado que la modifique o la borre.
/// <see cref="TenantId"/> es la compañía del lector (también en el backoffice). No guarda placa, VIN, documento, nombre, NIT, el
/// contenido ni el hash del archivo; el filtro de placa queda como <c>"placa_filtrada": true</c>. Su <see cref="Id"/> se imprime en el
/// PDF y en la hoja «Aviso» del Excel para rastrear un archivo sin guardarlo.
/// </summary>
public sealed class AccesoReporte : IEntidadDeEmpresa
{
    public Guid Id { get; init; } = Guid.CreateVersion7();

    /// <summary>Compañía del lector (la del token).</summary>
    public Guid TenantId { get; set; }

    /// <summary>Usuario que vio o descargó (claim <c>sub</c>, seudónimo).</summary>
    public Guid UsuarioId { get; init; }

    public AlcanceReporte Alcance { get; init; }

    public TipoReporte TipoReporte { get; init; }

    public FormatoReporte Formato { get; init; }

    /// <summary>Rango pedido, en días de Colombia (los dos inclusive).</summary>
    public DateOnly Desde { get; init; }

    public DateOnly Hasta { get; init; }

    /// <summary>jsonb con las claves de la lista blanca y valores de enum (<see cref="RegistroAccesosReporte"/>).</summary>
    public required string Filtros { get; init; }

    /// <summary>Compañía consultada; solo en <see cref="AlcanceReporte.BackofficeEmpresa"/>.</summary>
    public Guid? EmpresaConsultada { get; init; }

    /// <summary>Filas entregadas (la página o el archivo).</summary>
    public int Filas { get; init; }

    /// <summary>Compañías distintas entregadas.</summary>
    public int Empresas { get; init; }

    /// <summary>Momento del acceso; si no se indica, lo pone la base.</summary>
    public DateTimeOffset Fecha { get; init; }

    /// <summary>Antes de guardar: una fila de <c>accesos_reporte</c> modificada o borrada hace fallar todo el guardado (solo inserción).</summary>
    internal static void ValidarSoloInsercion(ChangeTracker cambios)
    {
        ArgumentNullException.ThrowIfNull(cambios);
        if (cambios.Entries<AccesoReporte>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
            throw new InvalidOperationException("diagnostico.accesos_reporte es de solo inserción: una fila no se modifica ni se borra (ADR-0088).");
    }
}

/// <summary>Desde dónde se leyó el reporte (columna <c>alcance</c>).</summary>
public enum AlcanceReporte
{
    Compania,
    BackofficeEmpresa,
    BackofficeGlobal,
}

/// <summary>
/// Tipo de reporte, enum cerrado (contrato-api §2). En la base y en la ruta va en minúscula (<c>vehiculos</c>…), no con
/// <c>CodigosDeEnum</c>: <see cref="TiposDeReporte.Codigo"/>.
/// </summary>
public enum TipoReporte
{
    Vehiculos,
    Consultas,
    Saneamientos,
    Derivaciones,
    Consumo,
}

/// <summary>Formato entregado (columna <c>formato</c>).</summary>
public enum FormatoReporte
{
    Pantalla,
    Xlsx,
    Pdf,
}

/// <summary>Código en minúscula de <see cref="TipoReporte"/> (columna <c>tipo_reporte</c>, segmento <c>{tipo}</c> y nombre de archivo).</summary>
public static class TiposDeReporte
{
    public static string Codigo(TipoReporte tipo) => tipo switch
    {
        TipoReporte.Vehiculos => "vehiculos",
        TipoReporte.Consultas => "consultas",
        TipoReporte.Saneamientos => "saneamientos",
        TipoReporte.Derivaciones => "derivaciones",
        TipoReporte.Consumo => "consumo",
        _ => throw new ArgumentOutOfRangeException(nameof(tipo)),
    };

    /// <summary>El tipo cuyo código es <paramref name="codigo"/> (exacto); false si no hay ninguno.</summary>
    public static bool TryLeer(string? codigo, out TipoReporte tipo)
    {
        foreach (var t in Enum.GetValues<TipoReporte>())
        {
            if (string.Equals(Codigo(t), codigo, StringComparison.Ordinal))
            {
                tipo = t;
                return true;
            }
        }

        tipo = default;
        return false;
    }
}
