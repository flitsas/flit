using Flit.Modules.Security.Application.Auth;
using Flit.Modules.Security.Application.Products;

namespace Flit.Api.Authorization;

/// <summary>
/// Resultado de resolver el dominio de UNA petición (HU #12417 AC2, ADR-0060 D2). Lo construye
/// <see cref="Flit.Api.Middleware.DomainContextMiddleware"/> leyendo SOLO el sello
/// <c>X-Flit-Domain</c> (nunca un header/parámetro sin sellar) y queda en
/// <c>HttpContext.Items</c> bajo <see cref="Flit.Api.Middleware.DomainContextMiddleware.ItemKey"/>.
/// </summary>
/// <remarks>
/// HU #12968 (contrato v1 §5): <see cref="ProductCode"/> va al final y con valor por defecto, para que los
/// constructores existentes sigan compilando sin cambios.
/// </remarks>
public sealed record DomainContext(DomainKind Kind, string? Host, Guid? HeadTenantId, string ProductCode = ProductCodes.Plataforma)
{
    /// <summary>Sin sello, sello vacío o host reservado de FLIT (AC2, fail-closed).</summary>
    public static DomainContext Flit(string? host, string productCode = ProductCodes.Plataforma) =>
        new(DomainKind.Flit, host, null, productCode);

    public static DomainContext Network(string host, Guid headTenantId, string productCode = ProductCodes.Plataforma) =>
        new(DomainKind.Network, host, headTenantId, productCode);
}
