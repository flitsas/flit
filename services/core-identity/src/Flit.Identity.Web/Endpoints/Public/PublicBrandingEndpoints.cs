using System.Diagnostics;
using Flit.Admin.Application.Companies.Branding;
using Flit.Admin.Application.Companies.Branding.GetPublicBrandLogo;
using Flit.Admin.Application.Companies.Branding.ResolvePublicBranding;
using Flit.Api.RateLimiting;
using Flit.Modules.Security.Application.Auth;
using Microsoft.Extensions.Options;

namespace Flit.Api.Endpoints.Public;

/// <summary>
/// Endpoints PÚBLICOS de identidad de marca (HU #12418, Feature #12366, ADR-0060 D2). Contrato:
/// <c>.claude/state/marca-blanca/diseno/contratos-api.md</c> §1. Sin auth ni tenant header — el
/// dominio sale EXCLUSIVAMENTE del sello que puebla <c>DomainContextMiddleware</c>
/// (<see cref="IDomainContextAccessor"/>), nunca de un parámetro o cabecera que mande el cliente
/// (AC1). Misma forma y mismo código para marca publicada y para TODO caso negativo (AC2/AC3):
/// nunca hay forma de distinguir un dominio inexistente de uno configurado.
/// </summary>
internal static class PublicBrandingEndpoints
{
    internal static IEndpointRouteBuilder MapPublicBrandingEndpoints(this IEndpointRouteBuilder app)
    {
        // GET identidad de marca (AC1-AC3, AC6-AC9): SIEMPRE 200, misma forma en todos los casos.
        app.MapGet("/api/v1/public/branding", async Task<IResult> (
            IDomainContextAccessor domainContext,
            ResolvePublicBrandingHandler handler,
            IOptions<PublicBrandingOptions> options,
            HttpResponse response,
            CancellationToken ct) =>
        {
            var stopwatch = Stopwatch.StartNew();

            var result = await handler.HandleAsync(
                domainContext.Kind == DomainKind.Network,
                domainContext.HeadTenantId,
                ct).ConfigureAwait(false);

            await ApplyMinResponseTimeAsync(stopwatch, options.Value.MinResponseMs, ct).ConfigureAwait(false);

            response.Headers.CacheControl = "public, max-age=60";
            response.Headers["Vary"] = "X-Flit-Domain";

            return Results.Ok(result);
        }).WithName("GetPublicBranding").AllowAnonymous().RequireRateLimiting(PublicBrandingRateLimit.PolicyName);

        // GET logotipo por versión, streaming (AC4): URL inmutable por logoId. 404 sin cuerpo
        // indistinguible entre id inexistente, borrado o de una cabeza que dejó de ser MARCA_BLANCA.
        app.MapGet("/api/v1/public/branding/logos/{logoId:guid}", async Task<IResult> (
            Guid logoId,
            HttpRequest request,
            HttpResponse response,
            GetPublicBrandLogoHandler handler,
            CancellationToken ct) =>
        {
            var ifNoneMatchHeader = request.Headers.IfNoneMatch.ToString();
            var ifNoneMatch = string.IsNullOrWhiteSpace(ifNoneMatchHeader) ? null : ifNoneMatchHeader;

            var result = await handler.HandleAsync(logoId, ifNoneMatch, ct).ConfigureAwait(false);
            if (!result.Found)
            {
                return Results.StatusCode(StatusCodes.Status404NotFound);
            }

            response.Headers.CacheControl = "public, max-age=31536000, immutable";
            response.Headers.ETag = $"\"{result.Sha256}\"";
            response.Headers["X-Content-Type-Options"] = "nosniff";

            return result.IsNotModified
                ? Results.StatusCode(StatusCodes.Status304NotModified)
                : Results.Stream(result.Content!, result.ContentType);
        }).WithName("GetPublicBrandLogo").AllowAnonymous().RequireRateLimiting(PublicBrandingRateLimit.PolicyName);

        return app;
    }

    /// <summary>AC2 — relleno opcional de tiempo mínimo (0 por defecto): comparte la MISMA rama de
    /// código para positivo y negativo, así que normalmente no hace falta; queda cableado por si la
    /// suite de paridad #12429 exige un umbral más estricto.</summary>
    private static async Task ApplyMinResponseTimeAsync(Stopwatch stopwatch, int minResponseMs, CancellationToken ct)
    {
        if (minResponseMs <= 0)
        {
            return;
        }

        var remaining = minResponseMs - stopwatch.ElapsedMilliseconds;
        if (remaining > 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(remaining), ct).ConfigureAwait(false);
        }
    }
}
