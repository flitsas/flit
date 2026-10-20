using Flit.Api.Consultas;

namespace Flit.Api.Endpoints.SuperAdmin;

/// <summary>
/// Consumo de consultas de una empresa (Epic #13316, HU #13345): totales por producto y fuente entre dos fechas, para
/// controlar el costo de los proveedores. Solo SuperAdmin (lo exige el grupo). Los datos viven en Consultas; core-api
/// los pide por gRPC. Si Consultas no responde, 503 <c>CONSULTAS_NO_DISPONIBLE</c>.
/// </summary>
internal static class ConsultasConsumoEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapGet("/consultas/consumo", async (
            Guid tenantId,
            DateTimeOffset desde,
            DateTimeOffset hasta,
            IConsultasConsumo consumo,
            CancellationToken ct) =>
        {
            if (hasta <= desde)
                return Problem(StatusCodes.Status400BadRequest, "RANGO_INVALIDO", "«desde» tiene que ser anterior a «hasta».");

            try
            {
                return Results.Ok(await consumo.ObtenerAsync(tenantId, desde, hasta, ct).ConfigureAwait(false));
            }
            catch (ConsultasNoDisponibleException ex)
            {
                return Problem(StatusCodes.Status503ServiceUnavailable, "CONSULTAS_NO_DISPONIBLE", ex.Message);
            }
        }).WithName("GetConsultasConsumo");
    }

    private static IResult Problem(int status, string code, string detail) =>
        Results.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
