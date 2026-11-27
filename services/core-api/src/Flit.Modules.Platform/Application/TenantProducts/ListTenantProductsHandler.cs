using Flit.Modules.Platform.Domain.Products;
using Flit.Modules.Platform.Domain.TenantProducts;
using Flit.Modules.Security.Application.Products;

namespace Flit.Modules.Platform.Application.TenantProducts;

/// <summary>
/// Productos de una empresa tal como los ve el SuperAdmin: todo el catálogo salvo
/// <c>plataforma</c>, con su estado para esa empresa. Un producto sin fila sale apagado
/// (fail-closed). En <c>diagnostico</c> incluye si la API key del impuesto está configurada
/// (solo booleano; ADR-0092) y, HU #14711, si lo está el secreto del webhook, más el estado del alta de esa credencial. Base de <c>GET /admin/tenants/{tenantId}/products</c> (B-06).
/// </summary>
public sealed class ListTenantProductsHandler
{
    private readonly IProductCatalog _catalog;
    private readonly ITenantProductRepository _repository;
    private readonly ITenantProductCredentialRepository _credentials;
    private readonly ITenantProductProvisioningStore _provisioning;

    public ListTenantProductsHandler(
        IProductCatalog catalog,
        ITenantProductRepository repository,
        ITenantProductCredentialRepository credentials,
        ITenantProductProvisioningStore provisioning)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
        _provisioning = provisioning ?? throw new ArgumentNullException(nameof(provisioning));
    }

    public async Task<IReadOnlyList<TenantProductView>> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var products = await _catalog.ListAsync(cancellationToken).ConfigureAwait(false);
        var rows = (await _repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false))
            .ToDictionary(r => r.ProductCode, StringComparer.Ordinal);

        var impuestoConfigurada = await _credentials.ExistsAsync(
                tenantId,
                TenantProductCredentialCodes.Diagnostico,
                TenantProductCredentialCodes.ImpuestoApiKey,
                cancellationToken)
            .ConfigureAwait(false);

        // HU #14711: si el secreto del webhook del impuesto está configurado (solo booleano).
        var secretoConfigurado = await _credentials.ExistsAsync(
                tenantId,
                TenantProductCredentialCodes.Diagnostico,
                TenantProductCredentialCodes.ImpuestoWebhookSecret,
                cancellationToken)
            .ConfigureAwait(false);

        // HU #14694 (plan §1.2 R1): último estado reportado, solo para los productos con aprovisionamiento. HU #14711: y el del
        // alta de la credencial del impuesto de diagnostico.
        var provisioning = (await _provisioning.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false))
            .Where(p => ProductosConAprovisionamiento.Tiene(p.ProductCode)
                        || string.Equals(p.ProductCode, TenantProductCredentialCodes.Diagnostico, StringComparison.Ordinal))
            .ToDictionary(p => p.ProductCode, StringComparer.Ordinal);

        return products
            .Where(p => p.Code != ProductCodes.Plataforma)
            .Select(p =>
            {
                var esDiagnostico = string.Equals(p.Code, TenantProductCredentialCodes.Diagnostico, StringComparison.Ordinal);
                bool? impuesto = esDiagnostico ? impuestoConfigurada : null;
                bool? secreto = esDiagnostico ? secretoConfigurado : null;
                var estado = provisioning.GetValueOrDefault(p.Code);
                return rows.TryGetValue(p.Code, out var row)
                    ? new TenantProductView(p.Code, p.Name, row.Enabled, row.Notes, row.UpdatedAt, row.UpdatedBy, impuesto, estado, secreto)
                    : new TenantProductView(p.Code, p.Name, false, null, null, null, impuesto, estado, secreto);
            })
            .ToList();
    }
}

/// <summary>Un producto del catálogo con su estado para la empresa consultada.</summary>
public sealed record TenantProductView(
    string ProductCode,
    string Name,
    bool Enabled,
    string? Notes,
    DateTimeOffset? UpdatedAt,
    Guid? UpdatedBy,
    bool? ImpuestoApiKeyConfigurada = null,
    TenantProductProvisioning? Provisioning = null,
    bool? ImpuestoWebhookSecretConfigurada = null);
