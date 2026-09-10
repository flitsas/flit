using Flit.Admin.Application.Companies.NotificationDeliveryLogs.List;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Bitácora consultable de intentos de envío de notificación (HU #11363, Feature #11348). Módulo
/// Admin Compañías: <see cref="AdminAuthorization.AdminCompanyPolicy"/> +
/// <see cref="CompanyOwnTenantFilter"/> — el mismo par que protege
/// <c>AdminPersonalizedDocumentsEndpoints</c> (ADR-0033), sin política nueva.
/// <b>Regla de firma no negociable</b>: <c>Guid tenantId</c> va SIEMPRE primero en la firma del
/// handler — <see cref="CompanyOwnTenantFilter"/> toma el primer <c>Guid</c> de los argumentos del
/// endpoint para autorizar; invertir el orden autoriza contra el id equivocado.
/// </summary>
public static class AdminCompanyNotificationDeliveryLogsEndpoints
{
    public static IEndpointRouteBuilder MapAdminCompanyNotificationDeliveryLogsEndpoints(
        this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/companies/{tenantId:guid}/notification-delivery-logs")
            .RequireAuthorization(AdminAuthorization.AdminCompanyPolicy)
            .AddEndpointFilter<CompanyOwnTenantFilter>()
            .WithTags("Admin · Compañías · Bitácora de notificaciones");

        group.MapGet("", ListAsync)
            .WithName("AdminCompanyNotificationDeliveryLogsList")
            .WithSummary("Lista la bitácora de intentos de envío de notificación de una compañía, más recientes primero")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return app;
    }

    private static async Task<IResult> ListAsync(
        Guid tenantId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromServices] ListNotificationDeliveryLogsHandler handler,
        CancellationToken cancellationToken)
    {
        var logs = await handler.HandleAsync(tenantId, skip, take, cancellationToken).ConfigureAwait(false);
        return Results.Ok(new { logs });
    }
}
