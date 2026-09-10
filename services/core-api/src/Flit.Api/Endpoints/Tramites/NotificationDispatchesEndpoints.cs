using Flit.Tramites.Application.UseCases.ProcedureInstances.Notifications;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Flit.Api.Endpoints.Tramites;

/// <summary>
/// HU #11470 — visibilidad para el gestor de a quién no se pudo notificar (y el desenlace
/// de cada cupo). Correo siempre enmascarado.
/// </summary>
internal static class NotificationDispatchesEndpoints
{
    internal static IEndpointRouteBuilder MapTramitesNotificationDispatchesEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/tramites");

        group.MapGet("/instances/{id:guid}/notification-dispatches", async (
            Guid id,
            [FromHeader(Name = "X-Tenant-Id")] Guid? tenantId,
            GetNotificationDispatchesHandler handler,
            CancellationToken ct) =>
        {
            if (tenantId is null || tenantId == Guid.Empty)
                return Results.Problem(statusCode: 400, title: "Bad Request", detail: "Falta header X-Tenant-Id");

            var (result, error) = await handler.HandleAsync(id, tenantId.Value, ct);
            return error is "not_found"
                ? Results.Problem(statusCode: 404, title: "Not Found", detail: "Procedure instance not found.")
                : Results.Ok(result);
        }).WithName("GetProcedureInstanceNotificationDispatches");

        return app;
    }
}
