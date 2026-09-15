using Flit.Admin.Domain.Companies.Branding;

namespace Flit.Admin.Application.Companies.Branding.GetBranding;

/// <summary>Lectura de la marca de una cabeza (HU #12412 AC1/AC3). Sin regla de negocio: <c>null</c> → 404.</summary>
public sealed class GetBrandingHandler(ITenantBrandingRepository repository)
{
    private readonly ITenantBrandingRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<TenantBranding?> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await _repository.GetByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
}
