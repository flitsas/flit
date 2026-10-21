using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Tablero;

/// <summary>
/// Tablero del lote (HU #13743, F04-H1, plan §6.1 fila 04, MOD04), todo con <c>diagnostico.diagnosticos.ver</c> y la
/// visibilidad de <see cref="AccesoLote"/> (otra compañía o lote ajeno con PROPIOS: 404):
/// <list type="bullet">
/// <item><c>GET /diagnosticos/{id}/grilla?desde={version}</c>: grilla incremental (<see cref="ConsultaGrilla"/>); sin
/// <c>desde</c> es 0 (todo); negativo, 400 <c>DESDE_INVALIDO</c>.</item>
/// <item><c>POST /diagnosticos/{id}/cerrar-panel</c>: EN_PROCESO con ERROR → INCOMPLETO (<see cref="CierrePanel"/>); si no
/// procede, 409.</item>
/// <item><c>GET /diagnosticos/{id}/historial</c>: transiciones del lote (<see cref="HistorialLote"/>).</item>
/// </list>
/// Ningún endpoint edita el estado a mano: COMPLETADO lo pone el procesador (<see cref="RecalculadorEstado"/>).
/// </summary>
internal static class EndpointsTablero
{
    public const string RutaGrilla = DiagnosticosEndpoints.Ruta + "/{id:guid}/grilla";
    public const string RutaCierrePanel = DiagnosticosEndpoints.Ruta + "/{id:guid}/cerrar-panel";
    public const string RutaHistorial = DiagnosticosEndpoints.Ruta + "/{id:guid}/historial";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapGet(RutaGrilla, Grilla).RequirePermiso(Permisos.DiagnosticosVer).WithName("ObtenerGrillaDiagnostico");
        api.MapPost(RutaCierrePanel, Cerrar).RequirePermiso(Permisos.DiagnosticosVer).WithName("CerrarPanelDiagnostico");
        api.MapGet(RutaHistorial, Historial).RequirePermiso(Permisos.DiagnosticosVer).WithName("ObtenerHistorialDiagnostico");
    }

    public static IServiceCollection AddTablero(this IServiceCollection services)
    {
        services.AddScoped<ConsultaGrilla>();
        services.AddScoped<CierrePanel>();
        services.AddScoped<HistorialLote>();
        return services;
    }

    private static async Task<IResult> Grilla(Guid id, long? desde, ConsultaGrilla consulta, CancellationToken ct) =>
        Results.Ok(await consulta.ObtenerAsync(id, desde ?? 0, ct).ConfigureAwait(false));

    private static async Task<IResult> Cerrar(Guid id, CierrePanel cierre, CancellationToken ct) =>
        Results.Ok(await cierre.CerrarAsync(id, ct).ConfigureAwait(false));

    private static async Task<IResult> Historial(Guid id, HistorialLote historial, CancellationToken ct) =>
        Results.Ok(await historial.ObtenerAsync(id, ct).ConfigureAwait(false));
}
