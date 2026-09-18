using Flit.Admin.Domain.Companies.Domains;

namespace Flit.Admin.Application.Companies.Domains.GetDomain;

/// <summary>
/// Lectura del dominio de una red (HU #12416 AC1, #12427 AC1/AC2). Sin regla de negocio: <c>null</c> →
/// 404. Reutilizado por el endpoint SuperAdmin y por la autogestión de la cabeza (mismo patrón que
/// <c>GetBrandingHandler</c>).
/// </summary>
public sealed class GetDomainHandler(ITenantDomainRepository repository)
{
    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<TenantDomain?> HandleAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await _repository.GetByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
}
