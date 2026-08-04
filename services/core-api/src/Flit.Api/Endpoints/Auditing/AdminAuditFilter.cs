using System.Security.Claims;
using Flit.Admin.Application.Auditing;
using Flit.Api.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Flit.Api.Endpoints.Auditing;

/// <summary>
/// Endpoint filter que audita ÉXITO y FALLO de una operación administrativa/seguridad
/// (HU #10678, generalización de <see cref="ConfigAuditFailureFilter"/> que solo cubría el
/// fallo de configuración OT). Conoce el desenlace de la ruta (2xx vs 4xx/5xx o excepción),
/// deriva <c>result</c> del status HTTP y delega en <see cref="IAdminAuditWriter"/> —que
/// escribe en su propio scope— para que la traza sobreviva al rollback de la operación
/// principal.
///
/// <see cref="_module"/>, <see cref="_operation"/>, <see cref="_entityName"/> y
/// <see cref="_targetEntityType"/> se fijan al montar el filtro en cada ruta;
/// <see cref="_targetRouteKey"/> indica el nombre del route value (p. ej. <c>userId</c>) del
/// que se toma <c>target_entity_id</c>, o <c>null</c> si la ruta no tiene una entidad
/// objetivo puntual (p. ej. crear invitación). Best-effort: nunca rompe la respuesta.
/// </summary>
internal sealed class AdminAuditFilter : IEndpointFilter
{
    private readonly string _module;
    private readonly string _operation;
    private readonly string _entityName;
    private readonly string? _targetEntityType;
    private readonly string? _targetRouteKey;

    public AdminAuditFilter(
        string module,
        string operation,
        string entityName,
        string? targetEntityType = null,
        string? targetRouteKey = null)
    {
        _module = module;
        _operation = operation;
        _entityName = entityName;
        _targetEntityType = targetEntityType;
        _targetRouteKey = targetRouteKey;
    }

    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;

        try
        {
            var result = await next(context).ConfigureAwait(false);

            var statusCode = (result as IStatusCodeHttpResult)?.StatusCode;
            var isFailure = statusCode is >= 400;

            await AuditAsync(
                httpContext,
                isFailure ? AuditVocabulary.Results.Failure : AuditVocabulary.Results.Success,
                isFailure ? ResolveErrorCode(httpContext, statusCode!.Value) : null)
                .ConfigureAwait(false);

            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await AuditAsync(
                httpContext,
                AuditVocabulary.Results.Failure,
                ConfigAuditFailureContext.GetErrorCode(httpContext) ?? "unhandled_exception")
                .ConfigureAwait(false);
            throw;
        }
    }

    private async Task AuditAsync(HttpContext httpContext, string result, string? errorCode)
    {
        var writer = httpContext.RequestServices.GetRequiredService<IAdminAuditWriter>();
        var auditContext = httpContext.RequestServices.GetRequiredService<IAuditContextAccessor>();

        var entry = new AdminAuditEntry(
            TenantId: ResolveTenantId(httpContext.User),
            TenantType: null,
            Module: _module,
            EntityName: _entityName,
            Operation: _operation,
            Result: result,
            ErrorCode: errorCode,
            ActorUserId: ResolveUserId(httpContext.User),
            TargetEntityType: _targetEntityType,
            TargetEntityId: ResolveTargetEntityId(httpContext),
            ClientIp: auditContext.ClientIp,
            UserAgent: httpContext.Request.Headers.UserAgent.ToString() is { Length: > 0 } ua ? ua : null,
            OldValue: AdminAuditDetailContext.GetOldValue(httpContext),
            NewValue: AdminAuditDetailContext.GetNewValue(httpContext));

        await writer.WriteAsync(entry, httpContext.RequestAborted).ConfigureAwait(false);
    }

    private Guid? ResolveTargetEntityId(HttpContext httpContext)
    {
        if (_targetRouteKey is null)
            return null;

        if (!httpContext.Request.RouteValues.TryGetValue(_targetRouteKey, out var raw))
            return null;

        return Guid.TryParse(raw?.ToString(), out var id) ? id : null;
    }

    private static string ResolveErrorCode(HttpContext httpContext, int statusCode) =>
        ConfigAuditFailureContext.GetErrorCode(httpContext) ?? statusCode switch
        {
            StatusCodes.Status422UnprocessableEntity => "validation_error",
            StatusCodes.Status409Conflict => "conflict",
            StatusCodes.Status404NotFound => "not_found",
            StatusCodes.Status403Forbidden => "forbidden",
            StatusCodes.Status401Unauthorized => "unauthorized",
            StatusCodes.Status400BadRequest => "bad_request",
            >= 500 => "server_error",
            _ => "error",
        };

    private static Guid? ResolveTenantId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(AdminAuthorization.TenantIdClaimType), out var tenantId)
            ? tenantId
            : null;

    private static Guid? ResolveUserId(ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue("sub"), out var userId) ? userId : null;
}
