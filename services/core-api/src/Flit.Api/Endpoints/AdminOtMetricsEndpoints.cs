using System.Security.Claims;
using Flit.Admin.Application.OtMetrics;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.OtMetrics;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Flit.Api.Endpoints;

/// <summary>
/// Reportes del organismo de tránsito.
///
/// <para>Hasta ahora el módulo de reportes solo existía para la empresa gestora: el organismo usaba
/// FLIT sin ningún instrumento para ver su propia operación. Estos endpoints cierran esa brecha con
/// el eje invertido — un organismo mirando a las empresas que le radican.</para>
///
/// <para>Va bajo la policy <c>OtModule</c>, la misma que el resto de <c>/admin/ot/*</c>, con el
/// override <c>transitOfficeId</c> para que SuperAdmin pueda ver el reporte de un organismo concreto.</para>
/// </summary>
public static class AdminOtMetricsEndpoints
{
    public static IEndpointRouteBuilder MapAdminOtMetricsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app
            .MapGroup("/api/v1/admin/ot/metrics")
            .RequireAuthorization(AdminAuthorization.OtModulePolicy)
            .WithTags("Admin · OT · Reportes");

        group.MapGet("/operational", GetOperationalPanelAsync)
            .WithName("AdminOtMetricsOperational")
            .WithSummary("Panel operativo del organismo: movimiento del día, cola y antigüedad")
            .WithDescription("Describe el estado ACTUAL de la bandeja (no depende del rango) más el "
                + "movimiento del día calendario de Bogotá. El desglose de la cola solo expone las "
                + "esperas accionables por el organismo; el resto se agrupa en enEsperaDelCliente.")
            .Produces<OtOperationalPanelDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/performance", GetPerformanceAsync)
            .WithName("AdminOtMetricsPerformance")
            .WithSummary("Desempeño: revisores del organismo y calidad por empresa cliente")
            .Produces<OtPerformanceDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/rejection-reasons", GetRejectionReasonsAsync)
            .WithName("AdminOtMetricsRejectionReasons")
            .WithSummary("Motivos por los que el organismo rechaza")
            .WithDescription("El porcentaje es «% de rechazos que incluyen esta causal»: como un "
                + "rechazo puede llevar varias, la suma puede pasar del 100 %. Acompaña el promedio "
                + "de causales por rechazo, que delata si alguien está marcando todo.")
            .Produces<OtRejectionReasonsDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/drilldown", GetDrilldownAsync)
            .WithName("AdminOtMetricsDrilldown")
            .WithSummary("Trámites que componen un bloque del panel")
            .WithDescription("Abre un número del reporte: devuelve los trámites que lo forman, con "
                + "los mismos predicados con que se contó la tarjeta. 'total' es el conteo completo "
                + "y 'omitidos' dice cuántos quedaron fuera del tope de filas.")
            .Produces<OtDrilldownDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/report", GetReportAsync)
            .WithName("AdminOtMetricsReport")
            .WithSummary("Informe del periodo: estados del OT, tiempos y detalle por trámite")
            .WithDescription("El universo son los trámites RECIBIDOS en el rango (los que entraron a "
                + "'entregado'), no los decididos: solo así el desglose por estado cierra contra el "
                + "total. Los estados son la lectura desde el organismo — borrador y preparado no "
                + "aparecen porque el organismo nunca los vio.")
            .Produces<OtReportDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        group.MapGet("/client-companies", ListClientCompaniesAsync)
            .WithName("AdminOtMetricsClientCompanies")
            .WithSummary("Empresas cliente del organismo, para el filtro del reporte")
            .Produces<IReadOnlyList<OtClientCompanyOptionDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetDrilldownAsync(
        HttpContext httpContext,
        [FromServices] GetOtDrilldownHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] string? bucket = null,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? modalidad = null,
        [FromQuery] Guid? clientTenantId = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        // Se valida contra la lista cerrada de bloques: un bucket desconocido devolvería la cola
        // completa y el usuario creería estar viendo el detalle de la tarjeta que pulsó.
        if (!OtDrilldownBuckets.IsKnown(bucket))
        {
            return Results.BadRequest(new { error = "El parámetro 'bucket' no corresponde a ningún bloque del panel." });
        }

        var (context, error) = ResolveContext(
            httpContext, transitOfficeCatalog, from, to, modalidad, clientTenantId, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var result = await handler
            .HandleAsync(context.TenantId, context.Filter, bucket!, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> ListClientCompaniesAsync(
        HttpContext httpContext,
        [FromServices] ListOtClientCompaniesHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] Guid? transitOfficeId = null)
    {
        // Reusa la resolución común con un rango cualquiera: aquí solo interesan tenant y override.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (context, error) = ResolveContext(
            httpContext, transitOfficeCatalog, today, today, null, null, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var result = await handler
            .HandleAsync(context.TenantId, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> GetReportAsync(
        HttpContext httpContext,
        [FromServices] GetOtReportHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? modalidad = null,
        [FromQuery] Guid? clientTenantId = null,
        [FromQuery] Guid? transitOfficeId = null,
        [FromQuery] int? page = null,
        [FromQuery] int? pageSize = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool desc = true)
    {
        var (context, error) = ResolveContext(
            httpContext, transitOfficeCatalog, from, to, modalidad, clientTenantId, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var query = GetOtReportHandler.BuildQuery(context.Filter, page, pageSize, sortBy, desc);

        var result = await handler
            .HandleAsync(context.TenantId, query, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> GetOperationalPanelAsync(
        HttpContext httpContext,
        [FromServices] GetOtOperationalPanelHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? modalidad = null,
        [FromQuery] Guid? clientTenantId = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(
            httpContext, transitOfficeCatalog, from, to, modalidad, clientTenantId, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var result = await handler
            .HandleAsync(context.TenantId, context.Filter, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> GetPerformanceAsync(
        HttpContext httpContext,
        [FromServices] GetOtPerformanceHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? modalidad = null,
        [FromQuery] Guid? clientTenantId = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(
            httpContext, transitOfficeCatalog, from, to, modalidad, clientTenantId, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var result = await handler
            .HandleAsync(context.TenantId, context.Filter, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    private static async Task<IResult> GetRejectionReasonsAsync(
        HttpContext httpContext,
        [FromServices] GetOtRejectionReasonsHandler handler,
        [FromServices] ITransitOfficeCatalog transitOfficeCatalog,
        CancellationToken cancellationToken,
        [FromQuery] DateOnly? from = null,
        [FromQuery] DateOnly? to = null,
        [FromQuery] string? modalidad = null,
        [FromQuery] Guid? clientTenantId = null,
        [FromQuery] Guid? transitOfficeId = null)
    {
        var (context, error) = ResolveContext(
            httpContext, transitOfficeCatalog, from, to, modalidad, clientTenantId, transitOfficeId);
        if (error is not null)
        {
            return error;
        }

        var result = await handler
            .HandleAsync(context.TenantId, context.Filter, context.ScopedOfficeId, cancellationToken)
            .ConfigureAwait(false);

        return result is null ? NoTransitOffice() : Results.Ok(result);
    }

    // ── Resolución común de contexto ──────────────────────────────────────────────────────────

    private sealed record MetricsContext(Guid TenantId, OtMetricsFilter Filter, Guid? ScopedOfficeId);

    private static (MetricsContext Context, IResult? Error) ResolveContext(
        HttpContext httpContext,
        ITransitOfficeCatalog transitOfficeCatalog,
        DateOnly? from,
        DateOnly? to,
        string? modalidad,
        Guid? clientTenantId,
        Guid? transitOfficeId)
    {
        var empty = new MetricsContext(Guid.Empty, new OtMetricsFilter(default, default), null);

        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            return (empty, Results.Json(
                new { error = "Token inválido: falta claim tenant_id" },
                statusCode: StatusCodes.Status401Unauthorized));
        }

        var rangeError = OtMetricsQueryValidation.Validate(from, to);
        if (rangeError is not null)
        {
            return (empty, Results.BadRequest(new { error = rangeError }));
        }

        // Override de organismo: solo SuperAdmin, y solo si existe en el catálogo.
        Guid? scopedOfficeId = null;
        if (IsSuperAdmin(httpContext.User) && transitOfficeId is Guid officeId && officeId != Guid.Empty)
        {
            if (!transitOfficeCatalog.Exists(officeId))
            {
                return (empty, Results.BadRequest(
                    new { error = "transitOfficeId no existe en el catálogo OT" }));
            }

            scopedOfficeId = officeId;
        }

        var filter = new OtMetricsFilter(
            from!.Value,
            to!.Value,
            string.IsNullOrWhiteSpace(modalidad) ? null : modalidad.Trim(),
            clientTenantId);

        return (new MetricsContext(tenantId, filter, scopedOfficeId), null);
    }

    /// <summary>
    /// El tenant no tiene organismo asociado. Se responde 404 con el motivo concreto en vez de un
    /// reporte vacío: un panel en ceros haría pensar que no hay trabajo, cuando lo que falta es la
    /// configuración del perfil OT.
    /// </summary>
    private static IResult NoTransitOffice() =>
        Results.NotFound(new
        {
            error = "El usuario no tiene un organismo de tránsito asociado. "
                + "Configure el perfil OT del tenant para ver sus reportes.",
        });

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId) =>
        Guid.TryParse(user.FindFirstValue("tenant_id"), out tenantId);

    private static bool IsSuperAdmin(ClaimsPrincipal user) =>
        user.IsInRole(AdminAuthorization.SuperAdminRole)
        || string.Equals(
            user.FindFirstValue(AdminAuthorization.RoleClaimType),
            AdminAuthorization.SuperAdminRole,
            StringComparison.OrdinalIgnoreCase);
}
