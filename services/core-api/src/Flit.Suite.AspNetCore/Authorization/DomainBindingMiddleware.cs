using Flit.Admin.Application.Companies.Domains;
using System.Text.Json;
using Flit.Modules.Security.Application.Auth;

namespace Flit.Api.Authorization;

/// <summary>
/// Ligadura de la sesión al dominio de emisión (HU #12422 AC5, ADR-0060 D3). Compara el claim
/// <c>dom</c> del JWT (<c>"flit"</c> | host de una red) contra el <see cref="DomainContext"/> de la
/// petición ACTUAL (el sello <c>X-Flit-Domain</c> que fija <c>Flit.Gateway</c>, nunca un valor del
/// cliente sin sellar). Solo aplica a rutas AUTENTICADAS: va DESPUÉS de <c>UseAuthentication()</c> y
/// ANTES de <c>UseAuthorization()</c> — una petición anónima (<c>User.Identity.IsAuthenticated ==
/// false</c>) la atraviesa intacta. Tokens SIN claim <c>dom</c> (emitidos antes de esta HU) se tratan
/// como <c>"flit"</c> (paridad, sin logout masivo): siguen valiendo bajo el dominio de FLIT y dejan
/// de valer bajo el dominio de una red.
/// <para>
/// Límite declarado (Feature #12369, ADR-0060 D3): esta ligadura es una restricción de PRODUCTO
/// verificable, no una barrera criptográfica — mientras el Gateway no valide la firma del JWT
/// (Bug diferido, brief F7), un cliente que hable directamente con la capa interna podría evitarla.
/// </para>
/// </summary>
public sealed class DomainBindingMiddleware(RequestDelegate next)
{
    private const string DomainClaimType = "dom";
    private const string FlitDomainValue = "flit";

    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

    public async Task InvokeAsync(HttpContext context, IDomainContextAccessor domainContext, ITenantDomainResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domainContext);
        ArgumentNullException.ThrowIfNull(resolver);

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var claimValue = context.User.FindFirst(DomainClaimType)?.Value;
            var sessionDomain = string.IsNullOrWhiteSpace(claimValue) ? FlitDomainValue : claimValue;
            var sealedDomain = domainContext.Kind == DomainKind.Network
                ? domainContext.Host ?? FlitDomainValue
                : FlitDomainValue;

            if (!string.Equals(sessionDomain, sealedDomain, StringComparison.OrdinalIgnoreCase)
                && !await IsSameNetworkAsync(sessionDomain, domainContext, resolver, context.RequestAborted).ConfigureAwait(false))
            {
                await WriteMismatchAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// HU #12993 (FLIT Suite A-08) — con OIDC, la sesión de una red se abre en su hub (<c>dom</c> = dominio HUB) y se usa
    /// en los dominios de sus productos (<c>purpose</c>, B-08). Vale en cualquier dominio activo de la MISMA red; entre
    /// redes distintas, o entre una red y FLIT, sigue siendo <c>SESSION_DOMAIN_MISMATCH</c>.
    /// </summary>
    private static async Task<bool> IsSameNetworkAsync(
        string sessionDomain, IDomainContextAccessor domainContext, ITenantDomainResolver resolver, CancellationToken ct)
    {
        if (domainContext.Kind != DomainKind.Network || domainContext.HeadTenantId is not { } head
            || string.Equals(sessionDomain, FlitDomainValue, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var session = await resolver.ResolveAsync(sessionDomain.Trim().ToLowerInvariant(), ct).ConfigureAwait(false);
        return session.IsNetwork && session.HeadTenantId == head;
    }

    private static Task WriteMismatchAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "SESSION_DOMAIN_MISMATCH" }));
    }
}
