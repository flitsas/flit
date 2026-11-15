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
/// Las exportaciones B2/B3/B5/B6 (<c>/excel</c>, <c>/pdf</c>) se cablean aquí cuando BE-3 entregue el escritor y el freno.
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

        api.MapGroup(Ruta).RequireAuthorization(PoliticaBackoffice.Nombre).SoloEmpresaHabilitada().ConEmpresaExplicita()
            .MapGet("/{tipo}", PorEmpresaAsync).SinCacheEnRespuesta().WithName("ReporteEmpresaBackoffice");
    }

    private static IResult Catalogo(IOptions<OpcionesReportes> opciones) =>
        Results.Ok(CatalogoReportes.Dto(AlcanceReporte.BackofficeGlobal, opciones.Value));

    private static async Task<IResult> GlobalAsync(
        string tipo, HttpRequest request, DiagnosticoDb db, IEmpresasPlataforma plataforma, EmpresasDelReporte empresasDelReporte,
        MotorReportes motor, RegistroAccesosReporte registro, AuditoriaLecturaBackoffice auditoria, TimeProvider reloj, ILoggerFactory logs,
        CancellationToken ct)
    {
        var filtro = FiltroReporte.Leer(
            CatalogoReportes.LeerTipo(tipo, AlcanceReporte.BackofficeGlobal), request.Query, FiltroReporte.Hoy(reloj), rechazarEmpresa: true, paginar: true);
        var habilitadas = await EmpresasHabilitadasPlataforma.ListarAsync(plataforma, logs.CreateLogger(typeof(ReportesBackofficeEndpoints)), ct)
            .ConfigureAwait(false);
        var porId = habilitadas.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
        empresasDelReporte.Empresas = porId;
        var ids = porId.Keys.ToList();
        var contexto = new ContextoReporte(
            AlcanceReporte.BackofficeGlobal, AccesosReportesEntreEmpresas.De(db, ids), porId.ToDictionary(p => p.Key, p => p.Value.Nombre));
        return Results.Ok(await motor.PaginaAsync(
            contexto, filtro, (solicitud, empresas, c) => AuditarGlobalAsync(db, registro, auditoria, solicitud, empresas.Count > 0 ? empresas : ids, request, c), ct)
            .ConfigureAwait(false));
    }

    private static async Task<IResult> PorEmpresaAsync(
        string tipo, HttpContext http, DiagnosticoDb db, EmpresasDelReporte empresasDelReporte, MotorReportes motor, RegistroAccesosReporte registro,
        TimeProvider reloj, CancellationToken ct)
    {
        var filtro = FiltroReporte.Leer(
            CatalogoReportes.LeerTipo(tipo, AlcanceReporte.BackofficeEmpresa), http.Request.Query, FiltroReporte.Hoy(reloj), rechazarEmpresa: false, paginar: true);
        var empresa = EmpresaExplicita.De(http);
        var nombre = EmpresaHabilitada.Nombre(http);
        empresasDelReporte.Empresas = new Dictionary<Guid, EmpresaPlataforma>
        {
            [empresa] = new(empresa, nombre ?? string.Empty, EmpresaHabilitada.Nit(http)),
        };
        var contexto = new ContextoReporte(
            AlcanceReporte.BackofficeEmpresa, AccesosReportesEntreEmpresas.De(db, [empresa]),
            nombre is null ? null : new Dictionary<Guid, string> { [empresa] = nombre }, empresa);
        return Results.Ok(await motor.PaginaAsync(contexto, filtro, (solicitud, _, c) => registro.RegistrarAsync(solicitud, c), ct).ConfigureAwait(false));
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
