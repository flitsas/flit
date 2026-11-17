using Flit.Diagnostico.Api.Modulos.Diagnosticos;
using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// Reproceso y reintento manual de consultas, todo con <c>diagnostico.consultas.reprocesar</c> (admin de la compañía; el usuario
/// del producto recibe 403) y sin cuerpo (lo que venga se ignora: nunca se escriben datos de resultado, R-1):
/// <list type="bullet">
/// <item><c>POST /consultas/{cid}/reprocesar</c> (HU #13746, F03-H8, plan §6.1 fila 03): 202 con la celda en PENDIENTE; 404 si no
/// existe, es de otra compañía o de un lote que el usuario no ve; 409 si no es reprocesable (<see cref="Reproceso"/>).</item>
/// <item><c>POST /diagnosticos/{id}/vehiculos/{vid}/reintentar</c> (ajuste 2026-11-11 Punto 3): reencola las celdas pegadas de
/// esa placa; 200 <c>{ "reencoladas": n }</c> (<see cref="ReintentoPegadas"/>).</item>
/// <item><c>POST /diagnosticos/{id}/reintentar-pegadas</c> (Punto 3): reencola las celdas pegadas de todo el lote; 200
/// <c>{ "reencoladas": n }</c>.</item>
/// </list>
/// Los dos reintentos son idempotentes (la segunda pulsación responde <c>reencoladas: 0</c>) y responden 404 si el lote no se ve
/// (<see cref="Tablero.AccesoLote"/>) o el vehículo no es del lote.
/// </summary>
internal static class EndpointsReproceso
{
    public const string Ruta = "/consultas/{cid:guid}/reprocesar";
    public const string RutaReintentarVehiculo = DiagnosticosEndpoints.Ruta + "/{id:guid}/vehiculos/{vid:guid}/reintentar";
    public const string RutaReintentarPegadas = DiagnosticosEndpoints.Ruta + "/{id:guid}/reintentar-pegadas";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapPost(Ruta, Reprocesar).RequirePermiso(Permisos.ConsultasReprocesar).WithName("ReprocesarConsulta");
        api.MapPost(RutaReintentarVehiculo, ReintentarVehiculo).RequirePermiso(Permisos.ConsultasReprocesar).WithName("ReintentarPlaca");
        api.MapPost(RutaReintentarPegadas, ReintentarPegadas).RequirePermiso(Permisos.ConsultasReprocesar).WithName("ReintentarPegadasLote");
    }

    public static IServiceCollection AddReproceso(this IServiceCollection services)
    {
        services.AddScoped<Reproceso>();
        services.AddScoped<ReintentoPegadas>();
        return services;
    }

    private static async Task<IResult> Reprocesar(Guid cid, Reproceso reproceso, CancellationToken ct) =>
        Results.Accepted(value: await reproceso.ReprocesarAsync(cid, ct).ConfigureAwait(false));

    private static async Task<IResult> ReintentarVehiculo(Guid id, Guid vid, ReintentoPegadas reintento, CancellationToken ct) =>
        Results.Ok(await reintento.ReintentarVehiculoAsync(id, vid, ct).ConfigureAwait(false));

    private static async Task<IResult> ReintentarPegadas(Guid id, ReintentoPegadas reintento, CancellationToken ct) =>
        Results.Ok(await reintento.ReintentarLoteAsync(id, ct).ConfigureAwait(false));
}
