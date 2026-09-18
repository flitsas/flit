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

    public async Task InvokeAsync(HttpContext context, IDomainContextAccessor domainContext)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(domainContext);

        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var claimValue = context.User.FindFirst(DomainClaimType)?.Value;
            var sessionDomain = string.IsNullOrWhiteSpace(claimValue) ? FlitDomainValue : claimValue;
            var sealedDomain = domainContext.Kind == DomainKind.Network
                ? domainContext.Host ?? FlitDomainValue
                : FlitDomainValue;

            if (!string.Equals(sessionDomain, sealedDomain, StringComparison.OrdinalIgnoreCase))
            {
                await WriteMismatchAsync(context).ConfigureAwait(false);
                return;
            }
        }

        await _next(context).ConfigureAwait(false);
    }

    private static Task WriteMismatchAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "SESSION_DOMAIN_MISMATCH" }));
    }
}
