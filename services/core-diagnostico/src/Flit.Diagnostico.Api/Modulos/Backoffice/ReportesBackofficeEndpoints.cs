using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Reportes;
using Flit.Diagnostico.Api.Modulos.Reportes.Catalogo;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;
using Microsoft.Extensions.Options;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Reportes del backoffice (HU #14579, BE-4; contrato-api §2–§4; SP-1, SP-3, SP-4, SP-7, SP-8; ADR-0088). Todas las rutas exigen la
/// política de configuración del backoffice (<see cref="PoliticaBackoffice"/>: una compañía cliente con los claims armados a mano recibe
/// 403) y reutilizan el motor de la compañía (<see cref="MotorReportes"/>) con su propio contexto y su auditor:
/// <list type="bullet">
/// <item><b>B0</b> <c>GET /backoffice/reportes</c>: catálogo con los 5 tipos (consumo incluido); sin empresa y sin auditoría.</item>
/// <item><b>B1</b> <c>GET /backoffice/reportes/{tipo}/global</c>, sin empresa explícita (<c>?empresa=</c> da 400): solo las compañías
/// habilitadas de Identidad (una llamada; 503 si cae), <c>empresa_nombre</c> primero. Antes de descifrar deja, en un solo guardado, la fila
/// de <c>accesos_reporte</c> (BACKOFFICE_GLOBAL) y una de <c>lecturas_backoffice</c> por empresa de la página (con la página vacía, las
/// habilitadas). Si la auditoría falla no hay datos.</item>
/// <item><b>B4</b> <c>GET /backoffice/reportes/{tipo}?empresa=</c>: <c>.SoloEmpresaHabilitada()</c> antes de <c>ConEmpresaExplicita</c>
/// (que audita la lectura); deja además la fila de <c>accesos_reporte</c> BACKOFFICE_EMPRESA con <c>empresa_consultada</c>.</item>
/// </list>
/// Las exportaciones B2/B3 (<c>/{tipo}/global/excel|pdf</c>, nombre con <c>-global</c>) y B5/B6 (<c>/{tipo}/excel|pdf?empresa=</c>) exigen
/// además <c>diagnostico.reportes.descargar</c>, llevan <c>.DescargaSegura()</c> y reutilizan <see cref="ExportadorReportes"/> de BE-3 (freno
/// 429, tope 422 sin descifrar ni auditar) con el mismo contexto y el mismo auditor que la pantalla.
/// </summary>
internal static class ReportesBackofficeEndpoints
{
    public const string Reportes = "/reportes";
    public const string Ruta = BackofficeEndpoints.Grupo + Reportes;

    /// <summary>Servicios propios de los reportes del backoffice: el reporte de consumo y las compañías de la petición.</summary>
    public static IServiceCollection AddServicios(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddScoped<EmpresasDelReporte>();
        services.AddScoped<IReporte, ReporteConsumo>();
        return services;
    }

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        var sinEmpresa = api.MapGroup(BackofficeEndpoints.Grupo).RequireAuthorization(PoliticaBackoffice.Nombre);
        sinEmpresa.MapGet(Reportes, Catalogo).SinCacheEnRespuesta().WithName("CatalogoReportesBackoffice");
        sinEmpresa.MapGet(Reportes + "/{tipo}/global", GlobalAsync).SinCacheEnRespuesta().WithName("ReporteGlobalBackoffice");
        sinEmpresa.MapGet(Reportes + "/{tipo}/global/excel", (string tipo, HttpContext http, CancellationToken ct) => ExportarGlobalAsync(tipo, http, FormatoReporte.Xlsx, ct))
            .RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("ExportarReporteGlobalExcelBackoffice");
        sinEmpresa.MapGet(Reportes + "/{tipo}/global/pdf", (string tipo, HttpContext http, CancellationToken ct) => ExportarGlobalAsync(tipo, http, FormatoReporte.Pdf, ct))
            .RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("ExportarReporteGlobalPdfBackoffice");

        var porEmpresa = api.MapGroup(Ruta).RequireAuthorization(PoliticaBackoffice.Nombre).SoloEmpresaHabilitada().ConEmpresaExplicita();
        porEmpresa.MapGet("/{tipo}", PorEmpresaAsync).SinCacheEnRespuesta().WithName("ReporteEmpresaBackoffice");
        porEmpresa.MapGet("/{tipo}/excel", (string tipo, HttpContext http, CancellationToken ct) => ExportarPorEmpresaAsync(tipo, http, FormatoReporte.Xlsx, ct))
            .RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("ExportarReporteEmpresaExcelBackoffice");
        porEmpresa.MapGet("/{tipo}/pdf", (string tipo, HttpContext http, CancellationToken ct) => ExportarPorEmpresaAsync(tipo, http, FormatoReporte.Pdf, ct))
            .RequirePermiso(Permisos.ReportesDescargar).DescargaSegura().WithName("ExportarReporteEmpresaPdfBackoffice");
    }

    private static IResult Catalogo(IOptions<OpcionesReportes> opciones) =>
        Results.Ok(CatalogoReportes.Dto(AlcanceReporte.BackofficeGlobal, opciones.Value));

    private static async Task<IResult> GlobalAsync(string tipo, HttpContext http, MotorReportes motor, CancellationToken ct)
    {
        var (filtro, contexto, auditor) = await GlobalDeAsync(tipo, http, paginar: true, ct).ConfigureAwait(false);
        return Results.Ok(await motor.PaginaAsync(contexto, filtro, auditor, ct).ConfigureAwait(false));
    }

    private static async Task<IResult> ExportarGlobalAsync(string tipo, HttpContext http, FormatoReporte formato, CancellationToken ct)
    {
        var (filtro, contexto, auditor) = await GlobalDeAsync(tipo, http, paginar: false, ct).ConfigureAwait(false);
        return await http.RequestServices.GetRequiredService<ExportadorReportes>()
            .ExportarAsync(http.Response, contexto, filtro, formato, auditor, ct, global: true).ConfigureAwait(false);
    }

    private static async Task<IResult> PorEmpresaAsync(string tipo, HttpContext http, MotorReportes motor, CancellationToken ct)
    {
        var (filtro, contexto, auditor) = PorEmpresaDe(tipo, http, paginar: true);
        return Results.Ok(await motor.PaginaAsync(contexto, filtro, auditor, ct).ConfigureAwait(false));
    }

    private static Task<IResult> ExportarPorEmpresaAsync(string tipo, HttpContext http, FormatoReporte formato, CancellationToken ct)
    {
        var (filtro, contexto, auditor) = PorEmpresaDe(tipo, http, paginar: false);
        return http.RequestServices.GetRequiredService<ExportadorReportes>().ExportarAsync(http.Response, contexto, filtro, formato, auditor, ct);
    }

    /// <summary>
    /// Filtro, contexto y auditor de la vista global (B1–B3): el tipo y el filtro primero (400/404 sin llamar a Identidad), luego las
    /// compañías habilitadas (una llamada; 503 si cae) y las fuentes con <c>tenant_id IN</c> esas compañías.
    /// </summary>
    private static async Task<(FiltroReporte Filtro, ContextoReporte Contexto, AuditorReporte Auditor)> GlobalDeAsync(
        string tipo, HttpContext http, bool paginar, CancellationToken ct)
    {
        var servicios = http.RequestServices;
        var filtro = FiltroReporte.Leer(
            CatalogoReportes.LeerTipo(tipo, AlcanceReporte.BackofficeGlobal), http.Request.Query, FiltroReporte.Hoy(servicios.GetRequiredService<TimeProvider>()),
            rechazarEmpresa: true, paginar);
        var habilitadas = await EmpresasHabilitadasPlataforma.ListarAsync(
                servicios.GetRequiredService<IEmpresasPlataforma>(), servicios.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ReportesBackofficeEndpoints)), ct)
            .ConfigureAwait(false);
        var porId = habilitadas.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        servicios.GetRequiredService<EmpresasDelReporte>().Empresas = porId;
        var ids = porId.Keys.ToList();
        var db = servicios.GetRequiredService<DiagnosticoDb>();
        var registro = servicios.GetRequiredService<RegistroAccesosReporte>();
        var auditoria = servicios.GetRequiredService<AuditoriaLecturaBackoffice>();
        var contexto = new ContextoReporte(
            AlcanceReporte.BackofficeGlobal, AccesosReportesEntreEmpresas.De(db, ids), porId.ToDictionary(p => p.Key, p => p.Value.Nombre));
        AuditorReporte auditor = (solicitud, empresas, c) =>
            AuditarGlobalAsync(db, registro, auditoria, solicitud, empresas.Count > 0 ? empresas : ids, http.Request, c);
        return (filtro, contexto, auditor);
    }

    /// <summary>Filtro, contexto y auditor de la vista por compañía (B4–B6): la empresa ya validada y auditada por los filtros del grupo.</summary>
    private static (FiltroReporte Filtro, ContextoReporte Contexto, AuditorReporte Auditor) PorEmpresaDe(string tipo, HttpContext http, bool paginar)
    {
        var servicios = http.RequestServices;
        var filtro = FiltroReporte.Leer(
            CatalogoReportes.LeerTipo(tipo, AlcanceReporte.BackofficeEmpresa), http.Request.Query, FiltroReporte.Hoy(servicios.GetRequiredService<TimeProvider>()),
            rechazarEmpresa: false, paginar);
        var empresa = EmpresaExplicita.De(http);
        var nombre = EmpresaHabilitada.Nombre(http);
        servicios.GetRequiredService<EmpresasDelReporte>().Empresas = new Dictionary<Guid, EmpresaPlataforma>
        {
            [empresa] = new(empresa, nombre ?? string.Empty, EmpresaHabilitada.Nit(http)),
        };
        var registro = servicios.GetRequiredService<RegistroAccesosReporte>();
        var contexto = new ContextoReporte(
            AlcanceReporte.BackofficeEmpresa, AccesosReportesEntreEmpresas.De(servicios.GetRequiredService<DiagnosticoDb>(), [empresa]),
            nombre is null ? null : new Dictionary<Guid, string> { [empresa] = nombre }, empresa);
        AuditorReporte auditor = (solicitud, _, c) => registro.RegistrarAsync(solicitud, c);
        return (filtro, contexto, auditor);
    }

    /// <summary>
    /// SP-7 + SP-8 en la vista global: la fila de <c>accesos_reporte</c> y una de <c>lecturas_backoffice</c> por empresa, en el mismo
    /// <c>SaveChanges</c> (el registro solo agrega su fila; la auditoría guarda las dos). Sin empresas que auditar, guarda solo el acceso.
    /// </summary>
    internal static async Task<Guid> AuditarGlobalAsync(
        DiagnosticoDb db, RegistroAccesosReporte registro, AuditoriaLecturaBackoffice auditoria, SolicitudAccesoReporte solicitud,
        IReadOnlyCollection<Guid> empresas, HttpRequest request, CancellationToken ct)
    {
        var fila = registro.Agregar(solicitud);
        if (empresas.Count > 0)
            await auditoria.RegistrarVariasAsync(empresas, request, ct).ConfigureAwait(false);
        else
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return fila.Id;
    }
}
