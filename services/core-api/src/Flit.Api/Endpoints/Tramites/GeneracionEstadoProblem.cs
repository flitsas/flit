using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Traduce a <c>ProblemDetails</c> el veredicto del gate de generación documental del gestor
/// (HU #11051, <c>GeneracionDocumentalGestorGuard</c>). Compartido por los endpoints del gestor que
/// generan documentos: FUR, expediente consolidado e impronta.
/// </summary>
internal static class GeneracionEstadoProblem
{
    internal static IResult From(string error) => error switch
    {
        TramiteEstadoErrores.NoEncontrado => Results.Problem(
            statusCode: StatusCodes.Status404NotFound,
            title: "Not Found",
            detail: "Procedure instance not found."),
        TramiteEstadoErrores.GeneracionBloqueadaEstadoFinal => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: TramiteEstadoErrores.GeneracionBloqueadaEstadoFinal,
            detail: "El trámite ya está aprobado o anulado: su documentación es definitiva y no se "
                + "regenera. La documentación se regeneró al aprobar el trámite."),
        _ => Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "Conflict",
            detail: "No es posible generar documentación para este trámite."),
    };
}
