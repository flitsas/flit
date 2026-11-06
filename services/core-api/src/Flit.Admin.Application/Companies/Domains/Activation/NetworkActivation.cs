using Flit.Admin.Domain.Companies.Domains;
using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Domains.Activation;

/// <summary>
/// Estado de activación de una red. La red necesita el dominio HUB y uno por cada producto habilitado: un usuario de la
/// red no puede entrar por el hub de FLIT, así que un producto sin dominio propio queda inaccesible para él.
/// </summary>
public sealed record NetworkActivationStatus(IReadOnlyList<string> Products, IReadOnlyList<string> Pending, bool Active)
{
    public bool Ready => Pending.Count == 0 && !Active;

    public static NetworkActivationStatus From(IReadOnlyList<TenantDomain> domains, TenantProductFlags flags)
    {
        ArgumentNullException.ThrowIfNull(domains);
        ArgumentNullException.ThrowIfNull(flags);

        var products = EnabledProducts(flags);
        var pending = products
            .Prepend(TenantDomainPurposes.Hub)
            .Union(domains.Select(d => d.Purpose))
            .Where(purpose => !domains.Any(d => d.Purpose == purpose && IsReady(d)))
            .ToArray();
        var active = domains.Count > 0 && domains.All(d => d.Status == TenantDomainStatus.Active) && pending.Length == 0;
        return new NetworkActivationStatus(products, pending, active);
    }

    public static IReadOnlyList<string> EnabledProducts(TenantProductFlags flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var products = new List<string>();
        if (flags.Tramites) products.Add("tramites");
        if (flags.Comparendos) products.Add("comparendos");
        if (flags.Diagnostico) products.Add("diagnostico");
        return products;
    }

    private static bool IsReady(TenantDomain domain) =>
        domain.Status == TenantDomainStatus.Active
        || (domain.Status == TenantDomainStatus.Verified && domain.CertificateIssuedAt is not null);
}

public enum ActivateNetworkOutcome
{
    Activated,
    NotReady,
}

public sealed record ActivateNetworkResult(ActivateNetworkOutcome Outcome, IReadOnlyList<TenantDomain> Domains, NetworkActivationStatus Status);

/// <summary>
/// Activa los dominios de una red a pedido del SuperAdmin. Es un paso aparte de la emisión del certificado porque corta el
/// acceso: desde aquí, los usuarios de la red ya no inician sesión por el hub de FLIT. Repetirlo con la red activa no cambia nada.
/// </summary>
public sealed class ActivateNetworkHandler(ITenantDomainRepository repository, ITenantProductFlags productFlags, TimeProvider? timeProvider = null)
{
    private readonly ITenantDomainRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ITenantProductFlags _productFlags = productFlags ?? throw new ArgumentNullException(nameof(productFlags));
    private readonly TimeProvider _clock = timeProvider ?? TimeProvider.System;

    public async Task<ActivateNetworkResult> HandleAsync(Guid tenantId, Guid? changedBy, CancellationToken cancellationToken = default)
    {
        var flags = await _productFlags.GetAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var domains = await _repository.ListByTenantIdAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var status = NetworkActivationStatus.From(domains, flags);
        if (status.Pending.Count > 0)
        {
            return new ActivateNetworkResult(ActivateNetworkOutcome.NotReady, domains, status);
        }

        var activated = await _repository.ActivateNetworkAsync(tenantId, _clock.GetUtcNow(), changedBy, cancellationToken).ConfigureAwait(false);
        return new ActivateNetworkResult(ActivateNetworkOutcome.Activated, activated, NetworkActivationStatus.From(activated, flags));
    }
}
