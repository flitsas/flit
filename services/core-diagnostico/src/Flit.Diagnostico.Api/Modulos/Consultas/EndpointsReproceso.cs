using Flit.Diagnostico.Api.Plataforma;

namespace Flit.Diagnostico.Api.Modulos.Consultas;

/// <summary>
/// <c>POST /consultas/{cid}/reprocesar</c> (HU #13746, F03-H8, plan §6.1 fila 03) con <c>diagnostico.consultas.reprocesar</c>
/// (admin de la compañía; el usuario del producto recibe 403). Sin cuerpo: lo que venga se ignora, el reproceso nunca escribe
/// datos de resultado (R-1). 202 con la celda en PENDIENTE; 404 si no existe, es de otra compañía o de un lote que el usuario
/// no ve; 409 si no es reprocesable (<see cref="Reproceso"/>).
/// </summary>
internal static class EndpointsReproceso
{
    public const string Ruta = "/consultas/{cid:guid}/reprocesar";

    public static void Map(RouteGroupBuilder api)
    {
        ArgumentNullException.ThrowIfNull(api);
        api.MapPost(Ruta, Reprocesar).RequirePermiso(Permisos.ConsultasReprocesar).WithName("ReprocesarConsulta");
    }

    public static IServiceCollection AddReproceso(this IServiceCollection services)
    {
        services.AddScoped<Reproceso>();
        return services;
    }

    private static async Task<IResult> Reprocesar(Guid cid, Reproceso reproceso, CancellationToken ct) =>
        Results.Accepted(value: await reproceso.ReprocesarAsync(cid, ct).ConfigureAwait(false));
}
