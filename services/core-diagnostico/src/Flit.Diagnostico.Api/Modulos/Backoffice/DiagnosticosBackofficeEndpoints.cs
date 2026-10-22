using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Consultas;
using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Modulos.Tablero;
using Flit.Diagnostico.Api.Modulos.Vehiculos;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Persistence.Configuraciones;
using Flit.Diagnostico.Api.Plataforma;
using Flit.Diagnostico.Domain.Consultas;
using Flit.Diagnostico.Domain.Diagnosticos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Lotes de UNA compañía para el backoffice FLIT (HU #13799, F06-H2, F06-C5/C6/C7). Cuelgan del grupo <c>/backoffice</c>:
/// <see cref="PoliticaBackoffice"/> (<c>diagnostico.backoffice.ver</c> + <c>tenant_type=FLIT</c>) y la empresa explícita en
/// <c>?empresa=</c> (sin ella, 400 <c>EMPRESA_REQUERIDA</c>; con ella, la lectura queda en <c>lecturas_backoffice</c> antes de leer).
/// Toda lectura va por <see cref="LecturaEntreEmpresas"/> con esa empresa: un lote de otra compañía responde 404.
/// <list type="bullet">
/// <item><c>GET /backoffice/diagnosticos?empresa=&amp;estado=&amp;con_errores=&amp;pagina=&amp;tamano=</c>: lotes paginados, del más
/// reciente al más antiguo, con vehículos y celdas en ERROR.</item>
/// <item><c>GET /backoffice/diagnosticos/{id}/grilla?empresa=&amp;desde=</c>: la grilla de <see cref="ConsultaGrilla"/> con el
/// origen y el usuario manual de cada celda (<see cref="GrillaBackofficeDto"/>).</item>
/// <item><c>GET /backoffice/diagnosticos/{id}/historial?empresa=</c>: transiciones con el disparador y el usuario reales
/// (<c>MANUAL</c> incluido, decisión Q5); exige además <c>diagnostico.auditoria.ver</c>.</item>
/// </list>
/// </summary>
internal static class DiagnosticosBackofficeEndpoints
{
    public const string Ruta = "/diagnosticos";
    public const string RutaGrilla = Ruta + "/{id:guid}/grilla";
    public const string RutaHistorial = Ruta + "/{id:guid}/historial";
    public const string CodigoEstadoInvalido = "ESTADO_INVALIDO";
    public const string CodigoPaginacionInvalida = "PAGINACION_INVALIDA";
    public const int TamanoPorDefecto = 50;
    public const int TamanoMaximo = 200;

    public static void Map(RouteGroupBuilder backoffice)
    {
        backoffice.MapGet(Ruta, Listar).WithName("ListarDiagnosticosBackoffice");
        backoffice.MapGet(RutaGrilla, Grilla).WithName("ObtenerGrillaDiagnosticoBackoffice");
        backoffice.MapGet(RutaHistorial, Historial).RequirePermiso(Permisos.AuditoriaVer).WithName("ObtenerHistorialDiagnosticoBackoffice");
    }

    private static async Task<IResult> Listar(
        string? estado,
        [FromQuery(Name = "con_errores")] bool? conErrores,
        int? pagina,
        int? tamano,
        HttpContext http,
        DiagnosticoDb db,
        CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var numero = pagina ?? 1;
        var porPagina = tamano ?? TamanoPorDefecto;
        if (numero < 1)
            throw new ReglaException(CodigoPaginacionInvalida, "pagina empieza en 1.", StatusCodes.Status400BadRequest) { Campo = "pagina" };
        if (porPagina is < 1 or > TamanoMaximo)
            throw new ReglaException(CodigoPaginacionInvalida, $"tamano va de 1 a {TamanoMaximo}.", StatusCodes.Status400BadRequest) { Campo = "tamano" };

        var cabeceras = db.DeLaEmpresa<CabeceraDiagnostico>(empresa);
        if (!string.IsNullOrWhiteSpace(estado))
        {
            if (!CodigosDeEnum.TryLeer<EstadoDiagnostico>(estado, out var filtro))
                throw new ReglaException(CodigoEstadoInvalido, "estado es CREADO, EN_PROCESO, COMPLETADO o INCOMPLETO.", StatusCodes.Status400BadRequest) { Campo = "estado" };
            cabeceras = cabeceras.Where(c => c.Estado == filtro);
        }

        var vehiculos = db.DeLaEmpresa<VehiculoDiagnostico>(empresa);
        var errores = db.DeLaEmpresa<CeldaConsulta>(empresa).Where(c => c.Estado == EstadoCelda.Error);
        if (conErrores is { } soloConErrores)
            cabeceras = cabeceras.Where(c => errores.Any(e => e.DiagnosticoId == c.Id) == soloConErrores);

        var total = await cabeceras.CountAsync(ct).ConfigureAwait(false);
        var lotes = await cabeceras
            .OrderByDescending(c => c.CreadoEn).ThenByDescending(c => c.Id)
            .Skip((numero - 1) * porPagina).Take(porPagina)
            .Select(c => new
            {
                c.Id,
                c.Nombre,
                c.Estado,
                c.CreadoEn,
                Vehiculos = vehiculos.Count(v => v.DiagnosticoId == c.Id),
                Errores = errores.Count(e => e.DiagnosticoId == c.Id),
            })
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new ListaDiagnosticosBackofficeDto(empresa, numero, porPagina, total,
            [.. lotes.Select(l => new DiagnosticoBackofficeDto(l.Id, l.Nombre, CodigosDeEnum.Codigo(l.Estado), l.CreadoEn, l.Vehiculos, l.Errores))]));
    }

    private static async Task<IResult> Grilla(Guid id, long? desde, HttpContext http, DiagnosticoDb db, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var version = desde ?? 0;
        ConsultaGrilla.ValidarDesde(version);
        var cabecera = await CabeceraAsync(db, empresa, id, ct).ConfigureAwait(false);
        var grilla = await ConsultaGrilla.LeerAsync(new LecturaDeLaEmpresa(db, empresa), cabecera, version, ct).ConfigureAwait(false);
        return Results.Ok(GrillaBackofficeDto.De(empresa, grilla));
    }

    private static async Task<IResult> Historial(Guid id, HttpContext http, DiagnosticoDb db, CancellationToken ct)
    {
        var empresa = EmpresaExplicita.De(http);
        var cabecera = await CabeceraAsync(db, empresa, id, ct).ConfigureAwait(false);
        var filas = await db.DeLaEmpresa<HistorialEstadoDiagnostico>(empresa)
            .Where(h => h.DiagnosticoId == id)
            .OrderBy(h => h.En).ThenBy(h => h.Id)
            .ToListAsync(ct).ConfigureAwait(false);
        return Results.Ok(new HistorialLoteDto(cabecera.Id, CodigosDeEnum.Codigo(cabecera.Estado), [.. filas.Select(h => new TransicionLoteDto(
            h.EstadoAnterior is { } anterior ? CodigosDeEnum.Codigo(anterior) : null, CodigosDeEnum.Codigo(h.EstadoNuevo),
            CodigosDeEnum.Codigo(h.Disparador), h.UsuarioId, h.En))]));
    }

    /// <summary>La cabecera del lote en la empresa indicada; 404 <c>NO_ENCONTRADO</c> si no existe o es de otra compañía.</summary>
    private static async Task<CabeceraDiagnostico> CabeceraAsync(DiagnosticoDb db, Guid empresa, Guid id, CancellationToken ct) =>
        await db.DeLaEmpresa<CabeceraDiagnostico>(empresa).SingleOrDefaultAsync(c => c.Id == id, ct).ConfigureAwait(false)
            ?? throw ReglaException.NoEncontrado("El diagnóstico");

    /// <summary>Lectura de la grilla para UNA empresa explícita (<see cref="LecturaEntreEmpresas"/>), solo en este módulo.</summary>
    private sealed class LecturaDeLaEmpresa(DiagnosticoDb db, Guid empresa) : ILecturaTablero
    {
        public IQueryable<T> Filas<T>()
            where T : class, IEntidadDeEmpresa => db.DeLaEmpresa<T>(empresa);
    }
}
