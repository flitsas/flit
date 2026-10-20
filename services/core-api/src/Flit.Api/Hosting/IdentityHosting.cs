using Flit.Identity.Web;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Hosting;

/// <summary>
/// Rutas de identidad y salud de core-api (Epic #13217). Las rutas del login viven en Flit.Identity.Web; core-api las
/// mapea solo durante la transición, como respaldo del gateway de core-identity (HU #13235 las quita).
/// </summary>
internal static class IdentityHosting
{
    /// <summary>Las rutas del login (frontera §4): con la bandera del gateway apagada, core-api las sigue atendiendo.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app) =>
        app.MapFlitIdentityEndpoints(); // Epic #13217 (HU #13232): viven en Flit.Identity.Web

    /// <summary>
    /// <c>/health/ready</c> de core-api: la base responde y no le faltan migraciones que este código trae. El gateway lo
    /// consulta porque core-api es el respaldo del login durante la transición. <c>/health</c> sigue siendo solo «vivo».
    /// </summary>
    public static IEndpointRouteBuilder MapReadiness(this IEndpointRouteBuilder app)
    {
        app.MapGet("/health/ready", async (FlitDbContext db, CancellationToken ct) =>
        {
            if (!await db.Database.CanConnectAsync(ct).ConfigureAwait(false))
            {
                return Results.Json(new { status = "database_unreachable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            var pending = await db.Database.GetPendingMigrationsAsync(ct).ConfigureAwait(false);
            return pending.Any()
                ? Results.Json(new { status = "pending_migrations" }, statusCode: StatusCodes.Status503ServiceUnavailable)
                : Results.Ok(new { status = "ready" });
        }).AllowAnonymous();
        return app;
    }

    private static bool IsFlitType(Type type) =>
        type.Assembly.GetName().Name?.StartsWith("Flit.", StringComparison.Ordinal) == true;
}
