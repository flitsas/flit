using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Derivacion;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Dictamen;
using Flit.Diagnostico.Api.Modulos.Saneamiento;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.EntityFrameworkCore;
using SaneamientoEntidad = Flit.Diagnostico.Api.Modulos.Saneamiento.Saneamiento;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Los <see cref="IQueryable{T}"/> YA acotados de los que lee un reporte (HU #14579, BE-2; SP-4, SP-5, SP-6; patrón
/// <c>EscritorSaneamiento</c>). La proyección de <c>Modulos/Reportes/Catalogo</c> nunca abre <c>IgnoreQueryFilters</c>: recibe estas
/// fuentes y une cada tabla por <c>tenant_id</c> además del id. <see cref="DeLaCompaniaAsync"/> arma las de la compañía (filtro de tenant
/// normal y la visibilidad de <see cref="AccesoLote"/>: todo cuelga de las cabeceras visibles); el backoffice (BE-4) arma las suyas en
/// <c>Modulos/Backoffice</c> con accesos por tabla concreta y <c>tenant_id IN</c> las compañías habilitadas.
/// </summary>
internal sealed record FuentesReporte(
    IQueryable<CabeceraDiagnostico> Cabeceras,
    IQueryable<VehiculoDiagnostico> Vehiculos,
    IQueryable<PersonaVehiculo> Personas,
    IQueryable<CeldaConsulta> Consultas,
    IQueryable<IntentoConsulta> Intentos,
    IQueryable<DictamenVehiculo> Dictamenes,
    IQueryable<SaneamientoEntidad> Saneamientos,
    IQueryable<RenglonSaneamiento> Renglones,
    IQueryable<OperacionDerivacion> Operaciones,
    IQueryable<ItemDerivacion> Derivaciones)
{
    /// <summary>Las fuentes de la compañía del token, sin seguimiento; las cabeceras ya con la visibilidad del usuario (PROPIOS).</summary>
    public static async Task<FuentesReporte> DeLaCompaniaAsync(DiagnosticoDb db, UsuarioActual usuario, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var cabeceras = await AccesoLote.VisiblesAsync(db.Set<CabeceraDiagnostico>().AsNoTracking(), db, usuario, ct).ConfigureAwait(false);
        return new FuentesReporte(
            cabeceras,
            db.Set<VehiculoDiagnostico>().AsNoTracking(),
            db.Set<PersonaVehiculo>().AsNoTracking(),
            db.Set<CeldaConsulta>().AsNoTracking(),
            db.Set<IntentoConsulta>().AsNoTracking(),
            db.Set<DictamenVehiculo>().AsNoTracking(),
            db.Set<SaneamientoEntidad>().AsNoTracking(),
            db.Set<RenglonSaneamiento>().AsNoTracking(),
            db.Set<OperacionDerivacion>().AsNoTracking(),
            db.Set<ItemDerivacion>().AsNoTracking());
    }
}

/// <summary>
/// Lo que el motor necesita del ámbito: el alcance (decide columnas y si cuentan los intentos MANUAL), las fuentes ya acotadas, el
/// nombre de cada compañía (vista global) y la empresa consultada (BACKOFFICE_EMPRESA).
/// </summary>
internal sealed record ContextoReporte(
    AlcanceReporte Alcance,
    FuentesReporte Fuentes,
    IReadOnlyDictionary<Guid, string>? NombresEmpresa = null,
    Guid? EmpresaConsultada = null)
{
    /// <summary>En la compañía no hay rastro del soporte FLIT: ni la columna origen ni los intentos MANUAL (SP-16).</summary>
    public bool ConSoporte => Alcance != AlcanceReporte.Compania;
}

/// <summary>Un documento cifrado de una celda: id interno de la persona (solo para el log), tipo y bytes.</summary>
internal sealed record DocumentoCifrado(Guid? PersonaId, string Tipo, byte[] Cifrado);

/// <summary>Documentos de una columna de la fila, en orden; se unen con «; » una vez descifrados.</summary>
internal sealed record DocumentosPorDescifrar(string Clave, IReadOnlyList<DocumentoCifrado> Documentos);

/// <summary>
/// Una fila leída: la compañía, los valores por clave de columna y los documentos aún cifrados. Se descifran después de contar y de
/// registrar el acceso (<see cref="DescifradoDocumentos"/>), nunca antes.
/// </summary>
internal sealed class FilaReporte
{
    public required Guid Empresa { get; init; }

    public Dictionary<string, object?> Valores { get; } = new(StringComparer.Ordinal);

    public List<DocumentosPorDescifrar> Cifrados { get; } = [];
}

/// <summary>Un tipo de reporte: cuenta y lee sus filas de las fuentes acotadas del contexto, sin descifrar.</summary>
internal interface IReporte
{
    TipoReporte Tipo { get; }

    Task<int> ContarAsync(ContextoReporte contexto, FiltroReporte filtro, CancellationToken ct);

    /// <summary>Las filas en el orden del reporte; sin <paramref name="tomar"/>, todas (exportación, ya contada contra el tope).</summary>
    Task<IReadOnlyList<FilaReporte>> LeerAsync(ContextoReporte contexto, FiltroReporte filtro, int saltar, int? tomar, CancellationToken ct);
}

/// <summary>Formatos de valor comunes de las filas.</summary>
internal static class ValoresReporte
{
    public const int Lote = 500;

    /// <summary>Instante en hora Colombia (UTC−5 fijo); null se queda null.</summary>
    public static object? Instante(DateTimeOffset? valor) => valor?.ToOffset(Domain.Consultas.VigenciaDelDia.OffsetColombia);

    public static string? Codigo<T>(T? valor)
        where T : struct, Enum => valor is { } v ? Persistence.Configuraciones.CodigosDeEnum.Codigo(v) : null;

    /// <summary>Lotes de <see cref="Lote"/> elementos (consultas con <c>IN</c> acotado).</summary>
    public static IEnumerable<IReadOnlyList<T>> Lotes<T>(IReadOnlyList<T> lista)
    {
        for (var i = 0; i < lista.Count; i += Lote)
            yield return [.. lista.Skip(i).Take(Lote)];
    }
}
