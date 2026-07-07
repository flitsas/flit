using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Api.Endpoints.Auditing;

/// <summary>
/// Endpoint filter que audita el FALLO de una operación de configuración OT (RNF01, ADR-0024,
/// sección 3). Conoce el desenlace de la ruta (2xx vs 4xx/5xx o excepción) y, cuando no fue
/// exitoso, delega en <see cref="IAuditFailureWriter"/> —que escribe en su propio scope— para
/// que la traza <c>result = failure</c> sobreviva al rollback de la operación principal.
///
/// Grano por operación: <see cref="_entityName"/> + <see cref="_operation"/> se fijan al montar
/// el filtro en cada ruta. El <c>error_code</c> preciso lo aporta el endpoint vía
/// <see cref="ConfigAuditFailureContext"/>; si no, se deriva del status HTTP. Nunca transporta
/// valores sensibles.
/// </summary>
internal sealed class ConfigAuditFailureFilter : IEndpointFilter
{
    private readonly string _entityName;
    private readonly string _operation;

    public ConfigAuditFailureFilter(string entityName, string operation)
    {
        _entityName = entityName;
        _operation = operation;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        try
        {
            var result = await next(context).ConfigureAwait(false);

            if (result is IStatusCodeHttpResult { StatusCode: >= 400 } statusResult)
            {
                await AuditFailureAsync(
                    httpContext,
                    ResolveErrorCode(httpContext, statusResult.StatusCode!.Value))
                    .ConfigureAwait(false);
            }

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await AuditFailureAsync(
                httpContext,
                ConfigAuditFailureContext.GetErrorCode(httpContext) ?? "unhandled_exception")
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task AuditFailureAsync(HttpContext httpContext, string errorCode)
    {
        if (!TryResolveTenantId(httpContext.User, out var tenantId))
        {
            // Sin tenant (p. ej. 401) no hay configuración a la que atribuir el fallo.
            return;
        }

        var writer = httpContext.RequestServices.GetRequiredService<IAuditFailureWriter>();
        var auditContext = httpContext.RequestServices.GetRequiredService<IAuditContextAccessor>();

        await writer.WriteAsync(
            new AuditFailure(
                tenantId,
                _entityName,
                _operation,
                errorCode,
                auditContext.ClientIp,
                ResolveUserId(httpContext.User)),
            httpContext.RequestAborted).ConfigureAwait(false);
    }

    private static string ResolveErrorCode(HttpContext httpContext, int statusCode) =>
        ConfigAuditFailureContext.GetErrorCode(httpContext) ?? statusCode switch
        {
            StatusCodes.Status422UnprocessableEntity => "validation_error",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status400BadRequest => "bad_request",
            >= 500 => "server_error",
            _ => "error",
        };

    private static bool TryResolveTenantId(ClaimsPrincipal user, out Guid tenantId)
    {
        var claim = user.FindFirstValue(AdminAuthorization.TenantIdClaimType);
        return Guid.TryParse(claim, out tenantId);
    }

    private static Guid? ResolveUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var userId) ? userId : null;
}

/// <summary>
/// Puente para que un endpoint indique al <see cref="ConfigAuditFailureFilter"/> el
/// <c>error_code</c> preciso del fallo (p. ej. <c>campos_oficiales_no_editables</c>) sin
/// reescribir el flujo del handler. Debe ser un código estable, nunca un valor sensible.
/// </summary>
internal static class ConfigAuditFailureContext
{
    private const string ItemKey = "flit.audit.error_code";

    public static void SetErrorCode(HttpContext httpContext, string errorCode) =>
        httpContext.Items[ItemKey] = errorCode;

    public static string? GetErrorCode(HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
}
