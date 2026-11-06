using System.Text.Json;
using Flit.Api.Authorization;
using Flit.Modules.Security.Application.Auth;

namespace Flit.Api.Middleware;

/// <summary>
/// HU #14383 (Feature #14380, Épica #14297) — un dominio de portal de organismo no tiene inicio de sesión. Si la petición llega por
/// el dominio activo de un portal (<see cref="DomainKind.Portal"/>, resuelto del sello <c>X-Flit-Domain</c> del Gateway, nunca de un
/// encabezado del cliente), las rutas del login de FLIT (<c>/api/v1/auth</c>, <c>/connect</c> y <c>/.well-known</c>) responden 404 y
/// no se ejecutan: ni se abre el login ni se acota el acceso de ninguna red. Va justo después de <see cref="DomainContextMiddleware"/>.
/// </summary>
public sealed class PortalDomainGuardMiddleware(RequestDelegate next)
{
    public const string Codigo = "PORTAL_SIN_LOGIN";

    private static readonly string[] RutasDeLogin = ["/api/v1/auth", "/connect", "/.well-known"];

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (EsPortal(context) && Array.Exists(RutasDeLogin, r => context.Request.Path.StartsWithSegments(r, StringComparison.OrdinalIgnoreCase)))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                code = Codigo,
                message = "Este dominio es un portal público y no tiene inicio de sesión.",
            })).ConfigureAwait(false);
            return;
        }

        await next(context).ConfigureAwait(false);
    }

    private static bool EsPortal(HttpContext context) =>
        context.Items.TryGetValue(DomainContextMiddleware.ItemKey, out var valor)
        && valor is DomainContext { Kind: DomainKind.Portal };
}
