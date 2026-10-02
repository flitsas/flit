using Flit.Tramites.Application.UseCases.ProcedureInstances.Estados;
using Flit.Tramites.Domain.Tramites.Estados;
using Microsoft.AspNetCore.Http;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Bug #13194 (review PR #510, MAYOR-1) — ProblemDetails 409 del gate de firma, común a
/// <c>/transition</c>, <c>/submit</c>, <c>/enviar-al-ot</c> y su alias <c>/plate-flow/complete</c>.
/// <para>Contrato: además de <c>title</c> (código) y <c>detail</c> (texto), lleva la extensión
/// <c>partesSinFirma: [{ "parte": "comprador", "notificacion": "enviada" }]</c> cuando el ciclo de vida
/// registró el bloqueo en <see cref="UltimoBloqueoFirma"/>. <c>parte</c> ∈ {comprador, vendedor};
/// <c>notificacion</c> ∈ {no_requerida, enviada, ya_en_curso, fallida, no_configurada}. Sin PII.</para>
/// </summary>
internal static class FirmaPendienteProblem
{
    /// <summary>Nombre de la extensión del ProblemDetails.</summary>
    public const string Extension = "partesSinFirma";

    /// <summary>Detalle genérico cuando no hay partes registradas (sin PII).</summary>
    public const string DetalleGenerico =
        "No se permite enviar al organismo de tránsito un trámite sin firmar: falta la identidad aprobada y "
        + "vigente de alguna de las partes que firman.";

    /// <summary>
    /// 409 con <paramref name="title"/> y la extensión <see cref="Extension"/> si hay partes. Si no se da
    /// <paramref name="detalle"/>, se arma desde las partes (mismo texto estable que el ciclo de vida).
    /// </summary>
    public static IResult Crear(string title, string? detalle, IReadOnlyList<ParteSinFirma>? partes)
    {
        var detail = detalle
            ?? (partes is { Count: > 0 } ? FirmaGate.Detalle(partes) : DetalleGenerico);

        Dictionary<string, object?>? extensiones = partes is null
            ? null
            : new Dictionary<string, object?>
            {
                [Extension] = partes.Select(p => new { parte = p.Parte, notificacion = p.Notificacion }).ToList(),
            };

        return Results.Problem(statusCode: StatusCodes.Status409Conflict, title: title, detail: detail, extensions: extensiones);
    }
}
