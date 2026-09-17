using Flit.Api.Authorization;

namespace Flit.Api.Middleware;

/// <summary>
/// HU #12358 (Feature #12257) — aplica <see cref="TenantWriteGuard"/> a TODAS las rutas de escritura
/// sobre un trámite identificado en la ruta, en un solo punto y antes de que el endpoint enlace el
/// body: <c>POST|PUT|PATCH|DELETE</c> bajo <see cref="RuntimeInstancesPrefix"/> (crear/editar/avanzar/
/// firmar/anular/anexar/eliminar: 16 rutas en <c>ProcedureInstanceEndpoints</c> + actores, anexos,
/// firmas, firma diferida, FUR, consolidado, comercial, biometría, consultas, participantes, preflight,
/// RNMC) y bajo <see cref="AdminInstancesPrefix"/> (anular, estado, reasignar gestor, reenviar
/// validación, consolidado limpiar/cargar). Va DESPUÉS de <see cref="TenantEnforcementMiddleware"/>
/// (necesita el <c>TenantScope</c> de los Items) y del enrutamiento (necesita <c>RouteValues["id"]</c>).
/// <para>
/// Las escrituras sin id en la ruta (<c>POST /instances</c>, <c>/instances/from-consulta</c>) crean en
/// el tenant del token — la cabeza escribe sobre sí misma, que <c>CanWrite</c> permite — y
/// <c>POST /instances/pause-massive</c> (ids en el body) llama al guard desde el propio endpoint.
/// </para>
/// </summary>
public sealed class TenantWriteGuardMiddleware(RequestDelegate next)
{
    /// <summary>Prefijo runtime: la ruta lleva el id del trámite como <c>{id:guid}</c> justo después.</summary>
    public const string RuntimeInstancesPrefix = "/api/v1/tramites/instances";

    /// <summary>Prefijo de gestión avanzada (fuera del <see cref="TenantEnforcementMiddleware"/>).</summary>
    public const string AdminInstancesPrefix = "/api/v1/admin/tramites";

    private const string IdRouteKey = "id";

    public async Task InvokeAsync(HttpContext context)
    {
        if (IsGuardedWrite(context, out var procedureInstanceId))
        {
            var forbidden = await TenantWriteGuard.RejectIfCannotWriteAsync(context, procedureInstanceId, context.RequestAborted);
            if (forbidden is not null)
            {
                await forbidden.ExecuteAsync(context);
                return;
            }
        }

        await next(context);
    }

    /// <summary>Método de escritura + ruta de trámites + <c>{id}</c> de ruta con forma de Guid.</summary>
    public static bool IsGuardedWrite(HttpContext context, out Guid procedureInstanceId)
    {
        procedureInstanceId = Guid.Empty;
        if (!IsWriteMethod(context.Request.Method))
            return false;

        var path = context.Request.Path;
        if (!path.StartsWithSegments(RuntimeInstancesPrefix, StringComparison.OrdinalIgnoreCase)
            && !path.StartsWithSegments(AdminInstancesPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        return context.Request.RouteValues.TryGetValue(IdRouteKey, out var raw)
            && raw is string text
            && Guid.TryParse(text, out procedureInstanceId)
            && procedureInstanceId != Guid.Empty;
    }

    private static bool IsWriteMethod(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);
}
