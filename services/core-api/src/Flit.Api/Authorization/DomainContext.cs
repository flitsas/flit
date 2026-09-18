using Flit.Modules.Security.Application.Auth;

namespace Flit.Api.Authorization;

/// <summary>
/// Resultado de resolver el dominio de UNA petición (HU #12417 AC2, ADR-0060 D2). Lo construye
/// <see cref="Flit.Api.Middleware.DomainContextMiddleware"/> leyendo SOLO el sello
/// <c>X-Flit-Domain</c> (nunca un header/parámetro sin sellar) y queda en
/// <c>HttpContext.Items</c> bajo <see cref="Flit.Api.Middleware.DomainContextMiddleware.ItemKey"/>.
/// </summary>
public sealed record DomainContext(DomainKind Kind, string? Host, Guid? HeadTenantId)
{
    /// <summary>Sin sello, sello vacío o host reservado de FLIT (AC2, fail-closed).</summary>
    public static DomainContext Flit(string? host) => new(DomainKind.Flit, host, null);

    public static DomainContext Network(string host, Guid headTenantId) =>
        new(DomainKind.Network, host, headTenantId);
}
