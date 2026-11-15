using System.Diagnostics;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;

/// <summary>
/// Registra el acceso de una petición de reporte antes de entregar datos y devuelve su id (<c>acceso_id</c>). Recibe además las compañías
/// presentes en lo entregado: la compañía solo escribe <c>accesos_reporte</c>; el backoffice global (BE-4) agrega además una fila de
/// <c>lecturas_backoffice</c> por compañía, en el mismo guardado. Fail-closed: si lanza, no se entrega nada.
/// </summary>
internal delegate Task<Guid> AuditorReporte(SolicitudAccesoReporte solicitud, IReadOnlyCollection<Guid> empresas, CancellationToken ct);

/// <summary>
/// Motor genérico del módulo de reportes (HU #14579, BE-2/BE-3; diseno.md B1; contrato-api §3): resuelve el <see cref="IReporte"/> del
/// tipo, cuenta, lee la página sin descifrar, registra el acceso con <see cref="AuditorReporte"/> (fail-closed), y solo entonces descifra
/// los documentos y arma las filas con las claves exactas de la lista blanca del ámbito. No sabe de rutas ni de grupos: la compañía y el
/// backoffice (BE-4) le pasan su <see cref="ContextoReporte"/> y su auditor. Tiempo de comando de EF acotado (SP-11).
/// </summary>
internal sealed class MotorReportes(
    IEnumerable<IReporte> reportes, DescifradoDocumentos descifrado, IOptions<OpcionesReportes> opciones, DiagnosticoDb db, ILogger<MotorReportes> logger)
{
    private readonly Dictionary<TipoReporte, IReporte> _reportes = reportes.ToDictionary(r => r.Tipo);

    public OpcionesReportes Opciones => opciones.Value;

    /// <summary>La página del reporte (R1, B1, B4).</summary>
    public async Task<PaginaReporteDto> PaginaAsync(ContextoReporte contexto, FiltroReporte filtro, AuditorReporte auditar, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentNullException.ThrowIfNull(filtro);
        ArgumentNullException.ThrowIfNull(auditar);
        var reloj = Stopwatch.StartNew();
        var reporte = Reporte(filtro.Tipo);
        AcotarTiempo();
        var total = await reporte.ContarAsync(contexto, filtro, ct).ConfigureAwait(false);
        var filas = await reporte.LeerAsync(contexto, filtro, (filtro.Pagina - 1) * filtro.Tamano, filtro.Tamano, ct).ConfigureAwait(false);
        var (accesoId, empresas) = await AuditarAsync(contexto, filtro, FormatoReporte.Pantalla, filas, auditar, ct).ConfigureAwait(false);
        descifrado.Aplicar(filtro.Tipo, filas);
        var columnas = DefinicionReporte.Columnas(filtro.Tipo, contexto.Alcance);
        var codigo = TiposDeReporte.Codigo(filtro.Tipo);
        LogReportes.Entregado(logger, codigo, "PANTALLA", filas.Count, empresas, reloj.ElapsedMilliseconds, accesoId);
        return new PaginaReporteDto(
            codigo, CodigosDeEnum.Codigo(contexto.Alcance), filtro.Pagina, filtro.Tamano, total, accesoId, columnas,
            [.. filas.Select(f => Fila(columnas, f, contexto))]);
    }

    public IReporte Reporte(TipoReporte tipo) =>
        _reportes.TryGetValue(tipo, out var reporte)
            ? reporte
            : throw new Plataforma.ReglaException(CatalogoReportes.CodigoNoEncontrado, "El reporte pedido no existe.", StatusCodes.Status404NotFound);

    /// <summary>La fila con exactamente las claves de <paramref name="columnas"/>, en su orden; el nombre de la compañía en la global.</summary>
    internal static IReadOnlyDictionary<string, object?> Fila(IReadOnlyList<ColumnaReporte> columnas, FilaReporte fila, ContextoReporte contexto)
    {
        var salida = new Dictionary<string, object?>(columnas.Count, StringComparer.Ordinal);
        foreach (var columna in columnas)
        {
            salida[columna.Clave] = columna.Clave == DefinicionReporte.EmpresaNombre && !fila.Valores.ContainsKey(columna.Clave)
                ? contexto.NombresEmpresa?.GetValueOrDefault(fila.Empresa)
                : fila.Valores.GetValueOrDefault(columna.Clave);
        }

        return salida;
    }

    /// <summary>Acota el tiempo de comando de EF de esta petición (SP-11).</summary>
    internal void AcotarTiempo() => db.Database.SetCommandTimeout(TimeSpan.FromSeconds(Opciones.TimeoutSegundos));

    internal static async Task<(Guid AccesoId, int Empresas)> AuditarAsync(
        ContextoReporte contexto, FiltroReporte filtro, FormatoReporte formato, IReadOnlyList<FilaReporte> filas, AuditorReporte auditar, CancellationToken ct)
    {
        var empresas = filas.Select(f => f.Empresa).Distinct().ToList();
        var solicitud = new SolicitudAccesoReporte(
            contexto.Alcance, filtro.Tipo, formato, filtro.Desde, filtro.Hasta, filtro.ParaRegistro(), filas.Count, empresas.Count, contexto.EmpresaConsultada);
        var id = await auditar(solicitud, empresas, ct).ConfigureAwait(false);
        return (id, empresas.Count);
    }
}
