using Flit.Api.Endpoints;
using Flit.Api.Endpoints.Platform;
using Flit.Api.Endpoints.Public;
using Flit.Api.Identity;
using Flit.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Flit.Api.Hosting;

/// <summary>
/// Lo que distingue al papel <see cref="HostRole.Identity"/> (HU #13224): qué procesos en segundo plano corre y qué
/// rutas expone. El resto de la composición (servicios, middlewares, políticas) es la misma de <c>core-api</c>, así
/// que no puede desviarse. Ver <c>docs/suite/identidad-frontera.md</c> §3, §4 y §8.
/// </summary>
internal static class IdentityHosting
{
    /// <summary>Los únicos procesos en segundo plano propios que corren en <c>core-identity</c>.</summary>
    internal static readonly IReadOnlySet<Type> IdentityHostedServices = new HashSet<Type>
    {
        typeof(OidcClientSync),
        typeof(OidcPruningService),
    };

    /// <summary>
    /// Quita los procesos en segundo plano de FLIT (colas de correo, RUNT, Quipux, lotes, reportes, dominios…) salvo
    /// los de OIDC: ya corren en <c>core-api</c> y duplicarlos procesaría dos veces el mismo trabajo. Solo toca los
    /// tipos de ensamblados <c>Flit.*</c>: los del framework (el propio servidor web) se quedan.
    /// </summary>
    public static IServiceCollection RemoveBusinessHostedServices(this IServiceCollection services)
    {
        var business = services
            .Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType is { } type
                && IsFlitType(type)
                && !IdentityHostedServices.Contains(type))
            .ToList();
        foreach (var descriptor in business)
        {
            services.Remove(descriptor);
        }

        return services;
    }

    /// <summary>
    /// Las rutas que el login necesita para sobrevivir a una caída de <c>core-api</c> (frontera §4). Las mapean los dos
    /// papeles: con la bandera del gateway apagada, <c>core-api</c> las sigue atendiendo como hoy.
    /// </summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapAuthEndpoints(); // /api/v1/auth/*
        app.MapPlatformEndpoints(); // /api/v1/platform/* · HU #12966
        app.MapPublicBrandingEndpoints(); // /api/v1/public/branding* · HU #12418
        app.MapFlitOidcEndpoints(); // /connect/*, /api/v1/platform/issuers · HU #12990 (/connect/* con Suite:Oidc:Enabled)
        return app;
    }

    /// <summary>
    /// <c>/health/ready</c>: la base responde y no tiene migraciones pendientes. <c>core-identity</c> no migra, así que
    /// con un esquema más nuevo que su código no se declara listo. <c>/health</c> sigue siendo solo «vivo».
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
