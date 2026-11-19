using Flit.Diagnostico.Api.Modulos.Comun;
using Flit.Diagnostico.Api.Modulos.Estadisticas;
using Flit.Diagnostico.Api.Persistence;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Backoffice;

/// <summary>
/// Estadísticas del backoffice (HU #14579, BE-1; contrato-api §1, SP-1, SP-3, SP-4, SP-7; ADR-0088). Las dos rutas exigen la política
/// de configuración del backoffice y <c>diagnostico.volumetria.ver</c> (<see cref="ReglasEstadisticasYReportes.RequiereEstadisticasGlobales{TBuilder}"/>):
/// <list type="bullet">
/// <item><b>E2</b> <c>GET /backoffice/estadisticas/global</c>, sin empresa explícita (<c>?empresa=</c> da 400): lee las compañías
/// habilitadas de Identidad (una llamada; 503 si cae), calcula sobre <c>tenant_id IN (habilitadas)</c>, audita en
/// <c>lecturas_backoffice</c> en un guardado cada empresa que aporta (o todas las habilitadas si ninguna aporta) y solo entonces
/// responde; si la auditoría falla, no hay datos.</item>
/// <item><b>E3</b> <c>GET /backoffice/estadisticas?empresa=</c>, con <c>.SoloEmpresaHabilitada()</c> antes de
/// <c>ConEmpresaExplicita</c> (que audita): ámbito EMPRESA con el nombre de Identidad.</item>
/// </list>
/// Sin PII (SP-16). <c>Cache-Control: no-store</c>.
/// </summary>
internal static class EstadisticasBackofficeEndpoints
{
    public const string Ruta = BackofficeEndpoints.Grupo + "/estadisticas";
    public const string RutaGlobal = "/estadisticas/global";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGroup(BackofficeEndpoints.Grupo).RequiereEstadisticasGlobales()
            .MapGet(RutaGlobal, GlobalAsync).SinCacheEnRespuesta().WithName("ObtenerEstadisticasGlobalesBackoffice");
        api.MapGroup(Ruta).RequiereEstadisticasGlobales().SoloEmpresaHabilitada().ConEmpresaExplicita()
            .MapGet(string.Empty, PorEmpresaAsync).SinCacheEnRespuesta().WithName("ObtenerEstadisticasEmpresaBackoffice");
    }

    private static async Task<IResult> GlobalAsync(
        HttpRequest request, DiagnosticoDb db, IEmpresasPlataforma plataforma, AuditoriaLecturaBackoffice auditoria, TimeProvider tiempo,
        IConfiguration configuracion, ILoggerFactory logs, CancellationToken ct)
    {
        var filtro = FiltroEstadisticas.Leer(request.Query, tiempo, admiteEmpresa: false);
        var habilitadas = await EmpresasHabilitadasPlataforma.ListarAsync(plataforma, logs.CreateLogger(typeof(EstadisticasBackofficeEndpoints)), ct).ConfigureAwait(false);
        var ids = habilitadas.Select(e => e.Id).ToList();
        EstadisticasRegistro.AcotarTiempo(db, configuracion);
        var lecturas = await CalculadoraEstadisticas.LeerAsync(AccesosEstadisticasEntreEmpresas.De(db, ids), filtro, ct).ConfigureAwait(false);

        // SP-7: auditar antes de serializar; sin aporte en el rango, las habilitadas consultadas.
        var conDatos = lecturas.EmpresasConDatos;
        await auditoria.RegistrarVariasAsync(conDatos.Count > 0 ? conDatos : ids, request, ct).ConfigureAwait(false);

        var nombres = habilitadas.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().Nombre);
        return Results.Ok(CalculadoraEstadisticas.Armar(
            new AmbitoEstadisticasDto("GLOBAL", null, null), filtro, tiempo.GetUtcNow(), lecturas, CalculadoraEstadisticas.Top(lecturas, nombres)));
    }

    private static async Task<IResult> PorEmpresaAsync(HttpContext http, DiagnosticoDb db, TimeProvider tiempo, IConfiguration configuracion, CancellationToken ct)
    {
        var filtro = FiltroEstadisticas.Leer(http.Request.Query, tiempo, admiteEmpresa: true);
        var empresa = EmpresaExplicita.De(http);
        EstadisticasRegistro.AcotarTiempo(db, configuracion);
        var lecturas = await CalculadoraEstadisticas.LeerAsync(AccesosEstadisticasEntreEmpresas.De(db, [empresa]), filtro, ct).ConfigureAwait(false);
        return Results.Ok(CalculadoraEstadisticas.Armar(
            new AmbitoEstadisticasDto("EMPRESA", empresa, EmpresaHabilitada.Nombre(http)), filtro, tiempo.GetUtcNow(), lecturas, []));
    }
}
