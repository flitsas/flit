using Flit.Api.Authorization;
using Flit.Tramites.Application.UseCases.ConsolidadoLotes;
using Flit.Tramites.Domain.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #13385 (Feature #13307, diseño 09 §2.4/§4, CF-09/CF-10) — <c>POST /api/v1/consolidados/lotes/{loteId}/cancelacion</c>
/// (<c>CancelarLoteConsolidados</c>): un clic, sin cuerpo ni confirmación (Q-3).
/// <list type="bullet">
///   <item>202 <c>LoteConsolidados</c> en <c>cancelado</c> (también si ya lo estaba: idempotente, sin nueva auditoría).</item>
///   <item>403 sin <c>consolidado-masivo.download</c> (<c>RequirePermission</c>, antes de tocar nada).</item>
///   <item>404 si no existe o no es del <c>sub</c> (incluido un Super Admin con un lote ajeno).</item>
///   <item>409 <c>{error: "lote_terminado", estado}</c> si ya terminó.</item>
///   <item>503 <c>auditoria_no_registrada</c> si la transacción (con <c>lote_cancelado</c>) no se confirmó: el lote sigue activo.</item>
/// </list>
/// Ruta neutra: dueño = <c>sub</c>, fuera de <c>TenantEnforcementMiddleware.RuntimeScopedRoutes</c> y sin
/// <c>OtModulePolicy</c>, así que el Gestor, el Super Admin y el <c>ot_admin</c> (tenant OT) cancelan sus propios lotes.
/// </summary>
/// <remarks>
/// Uso de ejemplo:
/// <code>
/// POST /api/v1/consolidados/lotes/0199…/cancelacion
/// → 202 { "estado": "cancelado", "terminadoEn": "…", "expiraEn": "…", "partes": [] }
/// </code>
/// </remarks>
internal static partial class ConsolidadoLoteEndpoints
{
    internal const string SufijoCancelacion = "cancelacion";
    internal const string MensajeLoteTerminado = "La descarga ya terminó y no se puede cancelar.";
    internal const string MensajeCancelacionNoRegistrada = "No se pudo cancelar la descarga, intente de nuevo.";

    internal static IEndpointRouteBuilder MapConsolidadoLoteCancelacionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost($"{RutaLotes}/{{loteId:guid}}/{SufijoCancelacion}", CancelarAsync)
            .RequirePermission(ConsolidadoLotePermisos.Descargar)
            .WithName("CancelarLoteConsolidados")
            .WithSummary("Cancela el lote en curso del usuario (cualquier origen)")
            .Produces<LoteConsolidadosDto>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        return app;
    }

    private static async Task<IResult> CancelarAsync(
        HttpContext http, Guid loteId, CancelarLoteConsolidadosHandler handler, CancellationToken ct)
    {
        if (UsuarioDelToken(http.User) is not { } usuarioId)
            return Results.Unauthorized();

        var r = await handler.HandleAsync(
            new CancelarLoteConsolidadosCommand(
                loteId,
                usuarioId,
                RolDelSolicitante(http.User, CompanyTenantAccess.IsSuperAdmin(http.User)),
                http.Connection.RemoteIpAddress,
                UserAgent(http)),
            ct);

        return r.Estado switch
        {
            CancelarLoteEstado.Cancelado or CancelarLoteEstado.YaCancelado =>
                Results.Accepted($"{RutaLotes}/{loteId}", LoteConsolidadosDto.Desde(r.Lote!)),
            CancelarLoteEstado.Terminado => Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: ConsolidadoLoteErrores.LoteTerminado,
                detail: MensajeLoteTerminado,
                extensions: new Dictionary<string, object?>
                {
                    ["error"] = ConsolidadoLoteErrores.LoteTerminado,
                    ["estado"] = r.Lote!.Status,
                }),
            CancelarLoteEstado.NoRegistrado => Problema(StatusCodes.Status503ServiceUnavailable,
                ConsolidadoLoteErrores.AuditoriaNoRegistrada, MensajeCancelacionNoRegistrada),
            _ => Results.NotFound(),
        };
    }
}
