using Flit.Api.Authorization;
using Flit.Modules.Platform.Domain.Access;
using Flit.Modules.Security.Application.Products;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Flit.Api.Platform;

/// <summary>
/// <c>RequireProduct</c> para las rutas de Trámites de core-api (contrato v1 §4 y §9; HU #12966, B-06).
/// Si la empresa del usuario (o su cabeza) tiene <c>tramites</c> apagado, con <c>Suite:ProductAccess:Enforce</c>
/// encendida responde 403 <c>PRODUCT_NOT_ENABLED</c>; apagada, solo lo registra.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item>Es middleware y no una policy por grupo porque las rutas de Trámites están repartidas en decenas de
///   <c>Map*Endpoints</c>: así basta una línea en <c>Program.cs</c> (regla R5). Todo <c>/api/v1</c> es Trámites
///   salvo las rutas de plataforma de <see cref="PlatformPrefixes"/>.</item>
///   <item>Solo mira si el producto está encendido, no el rol: con el token actual todos los roles de un usuario
///   viajan juntos. El chequeo de rol por producto llega con el token por producto (A-07).</item>
///   <item>El SuperAdmin pasa siempre (contrato §2.1). Las peticiones sin sesión o sin empresa las resuelven los
///   middlewares y policies de siempre.</item>
///   <item>Caché en memoria de 30 s por empresa: apagar un producto tarda hasta eso en surtir efecto, hasta que
///   exista la invalidación por evento (C-01).</item>
/// </list>
/// </remarks>
public sealed partial class RequireProductMiddleware(RequestDelegate next, IOptionsMonitor<ProductAccessOptions> options, IMemoryCache cache, ILogger<RequireProductMiddleware> logger)
{
    /// <summary>Rutas de plataforma: no son de Trámites.</summary>
    internal static readonly string[] PlatformPrefixes =
    [
        "/api/v1/auth", "/api/v1/security", "/api/v1/superadmin", "/api/v1/platform", "/api/v1/public", "/api/v1/internal",
    ];

    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public async Task InvokeAsync(HttpContext context)
    {
        var user = context.User;
        var path = context.Request.Path;
        if (user.Identity?.IsAuthenticated != true
            || !path.StartsWithSegments("/api/v1", StringComparison.OrdinalIgnoreCase)
            || Array.Exists(PlatformPrefixes, p => path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase))
            || RequestTenantResolver.IsSuperAdmin(user)
            || !RequestTenantResolver.TryResolveTenantId(user, out var tenantId))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var enabled = await cache.GetOrCreateAsync((nameof(RequireProductMiddleware), tenantId, ProductCodes.Tramites), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var store = context.RequestServices.GetRequiredService<IProductAccessStore>();
            var chain = await store.GetTenantChainAsync(tenantId, context.RequestAborted).ConfigureAwait(false);
            if (chain.Count == 0)
                return false;
            var on = await store.GetTenantsWithProductEnabledAsync(chain, ProductCodes.Tramites, context.RequestAborted).ConfigureAwait(false);
            return chain.All(on.Contains);
        }).ConfigureAwait(false);

        if (enabled)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!options.CurrentValue.Enforce)
        {
            LogNotEnabled(logger, tenantId, ProductCodes.Tramites, path.Value);
            await next(context).ConfigureAwait(false);
            return;
        }

        await Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Producto no habilitado",
                detail: "La empresa no tiene Trámites habilitado.",
                extensions: new Dictionary<string, object?> { ["code"] = "PRODUCT_NOT_ENABLED", ["product"] = ProductCodes.Tramites })
            .ExecuteAsync(context).ConfigureAwait(false);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "RequireProduct (solo registra): la empresa {TenantId} no tiene {Product} encendido y pidió {Path}")]
    private static partial void LogNotEnabled(ILogger logger, Guid tenantId, string product, string? path);
}
