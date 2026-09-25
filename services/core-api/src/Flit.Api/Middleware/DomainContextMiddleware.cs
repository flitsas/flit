using Flit.Admin.Application.Companies.Domains;
using Flit.Admin.Domain.Companies.Domains;
using Flit.Api.Authorization;

namespace Flit.Api.Middleware;

/// <summary>
/// Puebla <see cref="DomainContext"/> (HU #12417 AC2, ADR-0060 D2) leyendo EXCLUSIVAMENTE el sello
/// <c>X-Flit-Domain</c> que fija <c>Flit.Gateway</c> (<c>DomainSealTransform</c>) — nunca un
/// parámetro ni otro header del cliente. Sin sello, sello vacío o host reservado de FLIT
/// (<c>Domains:Reserved</c>, <see cref="ReservedHosts"/>) ⇒ <see cref="DomainContext.Flit(string?)"/>.
/// Va ANTES de auth en el pipeline (Program.cs): el login (#12422) necesita el dominio de la
/// petición para ligar la sesión a la red, sin depender de un JWT todavía sin validar (Bug diferido).
/// </summary>
public sealed class DomainContextMiddleware(RequestDelegate next)
{
    /// <summary>Clave en <see cref="HttpContext.Items"/> del <see cref="DomainContext"/> resuelto.</summary>
    public const string ItemKey = "flit.domainContext";

    private const string SealedDomainHeader = "X-Flit-Domain";

    private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));

    public async Task InvokeAsync(HttpContext context, ITenantDomainResolver resolver, DomainOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);

        var rawHost = context.Request.Headers.TryGetValue(SealedDomainHeader, out var values)
            ? values.ToString()
            : null;

        context.Items[ItemKey] = await ResolveAsync(rawHost, resolver, options, context.RequestAborted)
            .ConfigureAwait(false);

        await _next(context).ConfigureAwait(false);
    }

    /// <summary>
    /// Expuesto para pruebas directas (HU #12417) sin levantar el pipeline HTTP completo.
    /// </summary>
    public static async Task<DomainContext> ResolveAsync(
        string? rawHost,
        ITenantDomainResolver resolver,
        DomainOptions options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(rawHost))
        {
            return DomainContext.Flit(null);
        }

        var host = rawHost.Trim().ToLowerInvariant();

        if (ReservedHosts.IsReserved(host, options.Reserved, options.Allowed))
        {
            return DomainContext.Flit(host);
        }

        var resolution = await resolver.ResolveAsync(host, cancellationToken).ConfigureAwait(false);
        return resolution.IsNetwork && resolution.HeadTenantId is { } headTenantId
            ? DomainContext.Network(host, headTenantId)
            : DomainContext.Flit(host);
    }
}
