using Flit.Tramites.Application.UseCases.ProcedureInstances;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// Firma a posteriori (HU #11196 / #11197): consultar si la opción aplica a una parte y marcar el
/// trámite para que se firme cuando el representante legal complete su validación de identidad.
/// </summary>
internal static class FirmaPosteriorEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesFirmaPosteriorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        // GET — ¿se ofrece la opción para esta parte y ya está marcada? (HU #11197, AC1/AC2/AC4)
        group.MapGet("/instances/{id:guid}/deferred-signature", async (
            Guid id,
            [FromQuery] string? parte,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            MarcarFirmaPosteriorHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.ConsultarAsync(id, tenantId.Value, parte, ct);
            return error switch
            {
                "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor)."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                _ => Results.Ok(result),
            };
        }).WithName("GetProcedureInstanceDeferredSignature");

        // POST — marca el trámite para firma a posteriori (HU #11196, AC1). Idempotente.
        group.MapPost("/instances/{id:guid}/deferred-signature", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            DeferredSignatureBody body,
            MarcarFirmaPosteriorHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, body?.Parte, ct);
            return error switch
            {
                "parte_invalida" => Results.Problem(statusCode: 400, title: "Bad Request", detail: "parte inválida (use comprador|vendedor)."),
                "not_found" => Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found."),
                "not_draft" => Results.Problem(statusCode: 409, title: "Conflict", detail: "Solo se puede diferir la firma en borrador o con subsanación activa."),
                "sin_actor" => Results.Problem(statusCode: 409, title: "Conflict", detail: "La parte aún no tiene actor registrado."),
                "no_aplica" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "La firma a posteriori solo aplica a partes de tipo persona jurídica."),
                "sin_representante" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El representante legal del trámite no tiene documento con el que validar su identidad."),
                "firma_disponible" => Results.Problem(statusCode: 422, title: "Unprocessable Entity", detail: "El representante ya tiene firma o identidad vigente: el trámite puede firmarse ahora."),
                _ => Results.Ok(result),
            };
        }).WithName("MarkProcedureInstanceDeferredSignature");

        return app;
    }

    /// <summary>Cuerpo de la marca: la parte cuya firma se difiere.</summary>
    internal sealed record DeferredSignatureBody(string? Parte);
}
