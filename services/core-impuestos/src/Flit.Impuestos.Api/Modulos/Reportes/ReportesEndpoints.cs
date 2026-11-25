using System.Globalization;
using System.Text.RegularExpressions;
using Flit.Impuestos.Api.Modulos.Backoffice;
using Flit.Impuestos.Api.Modulos.Catalogos;
using Flit.Impuestos.Api.Modulos.Comun;
using Flit.Impuestos.Api.Modulos.Configuracion;
using Flit.Impuestos.Api.Modulos.Credenciales;
using Flit.Impuestos.Api.Modulos.Estadisticas;
using Flit.Impuestos.Api.Modulos.Liquidaciones;
using Flit.Impuestos.Api.Persistence;
using Flit.Impuestos.Api.Plataforma;
using Flit.Impuestos.Domain.Liquidaciones;
using Microsoft.EntityFrameworkCore;

namespace Flit.Impuestos.Api.Modulos.Reportes;

/// <summary><c>CatalogosReportes</c> (delta 12 de la 1.1.0): departamentos de la caché de <c>/fuentes</c> y creadores (id y nombre).</summary>
public sealed record CatalogosReportesDto(IReadOnlyList<DepartamentoDto> Departamentos, IReadOnlyList<ReferenciaDto> Usuarios);

/// <summary>
/// Rutas de reportes de M14 (plan §5.1; impuestos.v1.yaml 1.1.0, deltas 9, 11 y 12), sin caché en el cliente (llevan PII):
/// <list type="bullet">
///   <item><c>GET /reportes/{tipo}</c> (<c>impuestos.reportes.ver</c>): página del reporte con sus <c>columnas</c>; filtros <c>desde,
///   hasta, tipoLiquidacion, estado, departamento, usuario, placa</c>, <c>page</c> y <c>pageSize</c> (1..200, 50 por defecto).</item>
///   <item><c>GET /reportes/{tipo}/excel</c> y <c>/pdf</c> (<c>impuestos.reportes.exportar</c>): los mismos filtros sin paginación
///   (delta 9); más filas que el tope ⇒ 422 <c>TOPE_EXPORTACION</c> (<see cref="TopesExportacion"/>).</item>
///   <item><c>GET /catalogos/reportes</c> (<c>impuestos.reportes.ver</c>): opciones de los filtros; departamentos vacíos (nunca 409 ni
///   503) si la compañía no está lista.</item>
/// </list>
/// Un tipo desconocido responde 404 <c>REPORTE_NO_ENCONTRADO</c>; un filtro inválido, 400 <c>FILTRO_INVALIDO</c>; la paginación
/// inválida, 400 <c>PAGINACION_INVALIDA</c>; el rango, 400 <c>RANGO_INVALIDO</c>, todos con el literal del contrato. <c>?empresa</c>
/// sigue <see cref="FiltroEstadisticas"/>: el Super Admin sin ella ve la vista global con <c>empresa{id, nombre}</c> en cada fila.
/// </summary>
/// <example>
/// <code>
/// builder.Services.AddEstadisticasYReportes();
/// ReportesEndpoints.Map(api);
/// </code>
/// </example>
internal static partial class ReportesEndpoints
{
    public const string Ruta = "/reportes";
    public const string RutaCatalogos = "/catalogos/reportes";
    public const string Recurso = "reportes";
    public const int PageSizePorDefecto = 50;
    public const int PageSizeMaximo = 200;

    /// <summary>Registro de M14: alcance de lectura, nombres de usuarios, consulta y rastro de los reportes.</summary>
    public static IServiceCollection AddEstadisticasYReportes(this IServiceCollection services)
    {
        services.AddScoped<FiltroEstadisticas>();
        services.AddScoped<NombresUsuarios>();
        services.AddScoped<ConsultaReportes>();
        services.AddScoped<RegistroAccesosReporte>();
        return services;
    }

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var grupo = api.MapGroup(Ruta).SinCacheEnRespuesta();
        grupo.MapGet("/{tipo}", LeerAsync).RequirePermiso(Permisos.ReportesVer).WithName("ObtenerReporteImpuestos");
        grupo.MapGet("/{tipo}/excel", (string tipo, HttpContext http, [AsParameters] Dependencias d, CancellationToken ct) =>
            ExportarAsync(tipo, FormatoExportacion.Excel, http, d, ct)).RequirePermiso(Permisos.ReportesExportar).WithName("ExportarReporteExcelImpuestos");
        grupo.MapGet("/{tipo}/pdf", (string tipo, HttpContext http, [AsParameters] Dependencias d, CancellationToken ct) =>
            ExportarAsync(tipo, FormatoExportacion.Pdf, http, d, ct)).RequirePermiso(Permisos.ReportesExportar).WithName("ExportarReportePdfImpuestos");
        api.MapGet(RutaCatalogos, CatalogosAsync).RequirePermiso(Permisos.ReportesVer).SinCacheEnRespuesta().WithName("ObtenerCatalogosReportesImpuestos");
    }

    /// <summary>Servicios de las rutas, en un solo parámetro (<c>[AsParameters]</c>).</summary>
    internal sealed record Dependencias(
        UsuarioActual Usuario, FiltroEstadisticas Filtro, AlmacenCredenciales Almacen, LecturaEntreEmpresas Lectura, ConsultaReportes Consulta,
        RegistroAccesosReporte Registro, TimeProvider Reloj);

    private static async Task<IResult> LeerAsync(string tipo, HttpContext http, [AsParameters] Dependencias d, CancellationToken ct)
    {
        var (definicion, filtro) = Leer(tipo, http.Request.Query, d, paginado: true);
        var alcance = await d.Filtro.ResolverAsync(http.Request.Query[EmpresaExplicita.Parametro], Recurso, ct).ConfigureAwait(false);
        await using var fuente = FuenteLiquidaciones.Abrir(alcance, d.Almacen, d.Lectura);
        var total = await ConsultaReportes.ContarAsync(fuente, definicion, filtro, ct).ConfigureAwait(false);
        var filas = await d.Consulta.LeerAsync(fuente, alcance, definicion, filtro, paginar: true, ct).ConfigureAwait(false);
        d.Registro.Registrar(definicion, "json", alcance, filas.Count);
        return Results.Ok(new PaginaReporteDto(definicion.Codigo, CatalogoReportes.Columnas(definicion, alcance.Global), filas, total, filtro.Page, filtro.PageSize));
    }

    private static async Task<IResult> ExportarAsync(string tipo, FormatoExportacion formato, HttpContext http, Dependencias d, CancellationToken ct)
    {
        var (definicion, filtro) = Leer(tipo, http.Request.Query, d, paginado: false);
        var alcance = await d.Filtro.ResolverAsync(http.Request.Query[EmpresaExplicita.Parametro], Recurso, ct).ConfigureAwait(false);
        await using var fuente = FuenteLiquidaciones.Abrir(alcance, d.Almacen, d.Lectura);
        TopesExportacion.Exigir(formato, await ConsultaReportes.ContarAsync(fuente, definicion, filtro, ct).ConfigureAwait(false));
        var filas = await d.Consulta.LeerAsync(fuente, alcance, definicion, filtro, paginar: false, ct).ConfigureAwait(false);
        TopesExportacion.Exigir(formato, filas.Count);
        var archivo = ArchivoReporte.Armar(definicion, filtro.Rango, CatalogoReportes.Columnas(definicion, alcance.Global), filas);
        d.Registro.Registrar(definicion, formato == FormatoExportacion.Excel ? "excel" : "pdf", alcance, filas.Count);
        return formato == FormatoExportacion.Excel
            ? Results.File(ExcelReporte.Generar(archivo, ct), ExcelReporte.ContentType, archivo.NombreArchivo("xlsx"))
            : Results.File(PdfReporte.Generar(archivo, ct), PdfReporte.ContentType, archivo.NombreArchivo("pdf"));
    }

    private static async Task<IResult> CatalogosAsync(
        HttpContext http, UsuarioActual usuario, FiltroEstadisticas filtro, AlmacenCredenciales almacen, LecturaEntreEmpresas lectura, NombresUsuarios nombres,
        CancellationToken ct)
    {
        var empresa = http.Request.Query[EmpresaExplicita.Parametro];
        FiltroEstadisticas.ValidarEmpresa(usuario, empresa);
        var alcance = await filtro.ResolverAsync(empresa, Recurso, ct).ConfigureAwait(false);
        await using var fuente = FuenteLiquidaciones.Abrir(alcance, almacen, lectura);
        var listo = alcance.Global || SincronizacionFuentes.EstaLista(await SincronizacionFuentes.EstadoAsync(fuente.Db, alcance.Empresas[0], ct).ConfigureAwait(false));
        var departamentos = listo
            ? (await SincronizacionFuentes.CacheAsync(fuente.Db, ct).ConfigureAwait(false)).Select(f => new DepartamentoDto(f.DepartamentoCodigo, f.Nombre, f.Activa)).ToList()
            : [];

        var creadores = await fuente.Lotes.AsNoTracking().Select(l => new { l.TenantId, l.CreadaPor, l.CreadaPorNombre }).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var resueltos = await nombres.ResolverAsync(creadores.Select(c => (c.TenantId, c.CreadaPor, c.CreadaPorNombre)), ct).ConfigureAwait(false);
        var usuarios = creadores.GroupBy(c => c.CreadaPor)
            .Select(g =>
            {
                var conNombre = g.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.CreadaPorNombre)) ?? g.First();
                return new ReferenciaDto(g.Key, resueltos.Nombre(conNombre.TenantId, g.Key, conNombre.CreadaPorNombre));
            })
            .OrderBy(u => u.Nombre, StringComparer.Ordinal).ThenBy(u => u.Id)
            .ToList();
        return Results.Ok(new CatalogosReportesDto(departamentos, usuarios));
    }

    /// <summary>
    /// El reporte y sus filtros, en el orden de las reglas: primero <c>?empresa</c> (403 antes que cualquier 400), después el tipo
    /// (404), el rango y cada filtro.
    /// </summary>
    private static (DefinicionReporte Definicion, FiltroReporte Filtro) Leer(string tipo, IQueryCollection query, Dependencias d, bool paginado)
    {
        FiltroEstadisticas.ValidarEmpresa(d.Usuario, query[EmpresaExplicita.Parametro]);
        var definicion = CatalogoReportes.De(tipo) ?? throw CatalogoReportes.NoEncontrado();
        var rango = FiltroEstadisticas.Rango(query, d.Reloj);
        var tipoLiquidacion = Enumerado<TipoLiquidacion>(query, "tipoLiquidacion");
        var estado = Enumerado<EstadoItem>(query, "estado");
        var departamento = Unico(query, "departamento");
        if (departamento is not null && !Departamento().IsMatch(departamento))
            throw ErroresLiquidaciones.Filtro("departamento");
        Guid? usuario = Unico(query, "usuario") is { } u ? (Guid.TryParse(u, out var id) && id != Guid.Empty ? id : throw ErroresLiquidaciones.Filtro("usuario")) : null;
        string? placa = Unico(query, "placa") is { } p ? (Placa.TryNormalizar(p, out var normalizada) ? normalizada : throw ErroresLiquidaciones.Filtro("placa")) : null;
        var page = paginado ? Entero(query, "page", 1, int.MaxValue, 1) : 1;
        var pageSize = paginado ? Entero(query, "pageSize", PageSizePorDefecto, PageSizeMaximo, 1) : PageSizeMaximo;
        return (definicion, new FiltroReporte(rango, tipoLiquidacion, estado, departamento, usuario, placa, page, pageSize));
    }

    /// <summary>El valor si viene una sola vez y no está vacío; null si no viene; 400 <c>FILTRO_INVALIDO</c> si se repite.</summary>
    private static string? Unico(IQueryCollection query, string campo)
    {
        var valores = query[campo];
        if (valores.Count > 1)
            throw ErroresLiquidaciones.Filtro(campo);
        return valores.Count == 0 || string.IsNullOrWhiteSpace(valores[0]) ? null : valores[0]!.Trim();
    }

    private static T? Enumerado<T>(IQueryCollection query, string campo)
        where T : struct, Enum
    {
        if (Unico(query, campo) is not { } codigo)
            return null;
        foreach (var v in Enum.GetValues<T>())
        {
            if (string.Equals(CodigosDeEnum.Codigo(v), codigo, StringComparison.Ordinal))
                return v;
        }

        throw ErroresLiquidaciones.Filtro(campo);
    }

    private static int Entero(IQueryCollection query, string campo, int porDefecto, int maximo, int minimo)
    {
        var valores = query[campo];
        if (valores.Count == 0 || string.IsNullOrEmpty(valores[0]))
            return porDefecto;
        return valores.Count == 1 && int.TryParse(valores[0], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= minimo && n <= maximo
            ? n
            : throw ErroresLiquidaciones.Paginacion(campo);
    }

    [GeneratedRegex("^[0-9]{2}$")]
    private static partial Regex Departamento();
}
