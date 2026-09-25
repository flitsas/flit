using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Queries.Domain.Tenancy;
using Microsoft.Extensions.Logging;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Lista efectiva de OT: inclusión (Concesión), exclusión (Marca Blanca) o grants propios (HU #12347).
/// </summary>
internal sealed partial class EffectiveTransitOfficeListResolver : IEffectiveTransitOfficeListResolver
{
    private readonly ICompanyHierarchyRepository _hierarchy;
    private readonly ITransitGrantRepository _grants;
    private readonly ITenantTransitOfficeBlockRepository _blocks;
    private readonly ITransitOfficeOperationalStatusReader _operationalStatus;
    private readonly ILogger<EffectiveTransitOfficeListResolver> _logger;

    public EffectiveTransitOfficeListResolver(
        ICompanyHierarchyRepository hierarchy,
        ITransitGrantRepository grants,
        ITenantTransitOfficeBlockRepository blocks,
        ITransitOfficeOperationalStatusReader operationalStatus,
        ILogger<EffectiveTransitOfficeListResolver> logger)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _operationalStatus = operationalStatus ?? throw new ArgumentNullException(nameof(operationalStatus));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<Guid>> ListEffectiveOfficeIdsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var info = await _hierarchy.GetHierarchyInfoAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (info is null)
        {
            return [];
        }

        if (info.ParentTenantId is null)
        {
            if (!info.IsGroupParent)
            {
                return await _grants.ListEnabledOfficeIdsAsync(tenantId, cancellationToken).ConfigureAwait(false);
            }

            return await ResolveHeadEffectiveAsync(tenantId, info.TenantType, cancellationToken)
                .ConfigureAwait(false);
        }

        var head = await _hierarchy
            .GetHierarchyInfoAsync(info.ParentTenantId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (head is null)
        {
            HierarchyResolutionFailed(_logger, tenantId, info.ParentTenantId.Value);
            return [];
        }

        if (!GroupKindCodes.TryParse(head.TenantType, out var kind))
        {
            HeadKindMissing(_logger, head.TenantId);
            return [];
        }

        return kind switch
        {
            GroupKind.Concesion => await _grants
                .ListEnabledOfficeIdsAsync(head.TenantId, cancellationToken)
                .ConfigureAwait(false),
            GroupKind.MarcaBlanca => await ListMarcaBlancaEffectiveAsync(head.TenantId, cancellationToken)
                .ConfigureAwait(false),
            _ => [],
        };
    }

    /// <summary>
    /// Bug #12912 — inverso de <see cref="ListEffectiveOfficeIdsAsync"/>, rama por rama:
    /// <list type="bullet">
    ///   <item>cliente suelto o cabeza Concesión: su grant propio habilitado;</item>
    ///   <item>hijo de Concesión: el grant de su cabeza (el propio no cuenta, AC3 de #12347);</item>
    ///   <item>cabeza Marca Blanca y sus hijos: el OT es operable y la cabeza no lo bloquea.</item>
    /// </list>
    /// Tres lecturas acotadas (grants del OT, jerarquía de red, bloqueos del OT) + el estado operativo
    /// del OT solo si hay alguna red Marca Blanca. La propiedad
    /// <c>officeId ∈ Effective(t) ⇔ t ∈ Inverse(officeId)</c> está cubierta por prueba de integración.
    /// </summary>
    public async Task<IReadOnlyList<Guid>> ListEffectiveTenantIdsForOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var holders = (await _grants
            .ListEnabledTenantIdsForOfficeAsync(transitOfficeId, cancellationToken)
            .ConfigureAwait(false)).ToHashSet();

        var infos = (await _hierarchy
            .ListNetworkHierarchyInfoAsync(holders, cancellationToken)
            .ConfigureAwait(false))
            .ToDictionary(i => i.TenantId);

        var result = new HashSet<Guid>();

        // Cliente suelto o cabeza Concesión con grant propio.
        foreach (var holder in holders)
        {
            if (!infos.TryGetValue(holder, out var info) || info.ParentTenantId is not null)
            {
                continue;
            }

            if (!info.IsGroupParent || KindOf(info.TenantType) == GroupKind.Concesion)
            {
                result.Add(holder);
            }
        }

        // Marca Blanca: solo se consulta el estado operativo y los bloqueos si hay alguna red MB.
        var hasMarcaBlanca = infos.Values.Any(i => KindOf(i.TenantType) == GroupKind.MarcaBlanca);
        var mbOperable = hasMarcaBlanca
            && await IsOperableAsync(transitOfficeId, cancellationToken).ConfigureAwait(false);
        var blockingHeads = mbOperable
            ? (await _blocks.ListBlockingHeadIdsAsync(transitOfficeId, cancellationToken)
                .ConfigureAwait(false)).ToHashSet()
            : [];

        foreach (var info in infos.Values)
        {
            if (info.ParentTenantId is null)
            {
                // Cabeza Marca Blanca (rama ResolveHeadEffectiveAsync).
                if (info.IsGroupParent
                    && mbOperable
                    && KindOf(info.TenantType) == GroupKind.MarcaBlanca
                    && !blockingHeads.Contains(info.TenantId))
                {
                    result.Add(info.TenantId);
                }

                continue;
            }

            // Hijo: decide la clase de su cabeza, no su grant propio.
            if (!infos.TryGetValue(info.ParentTenantId.Value, out var head))
            {
                continue;
            }

            var included = KindOf(head.TenantType) switch
            {
                GroupKind.Concesion => holders.Contains(head.TenantId),
                GroupKind.MarcaBlanca => mbOperable && !blockingHeads.Contains(head.TenantId),
                _ => false,
            };

            if (included)
            {
                result.Add(info.TenantId);
            }
        }

        return result.OrderBy(id => id).ToList();
    }

    private static GroupKind? KindOf(string? tenantType) =>
        GroupKindCodes.TryParse(tenantType, out var kind) ? kind : null;

    /// <summary>Mismo criterio de operable que <see cref="ListMarcaBlancaEffectiveAsync"/>.</summary>
    private async Task<bool> IsOperableAsync(Guid transitOfficeId, CancellationToken cancellationToken)
    {
        var office = await _operationalStatus.GetByIdAsync(transitOfficeId, cancellationToken)
            .ConfigureAwait(false);
        return office is { HasTenant: true, EstadoActivo: true };
    }

    private async Task<IReadOnlyList<Guid>> ResolveHeadEffectiveAsync(
        Guid headTenantId,
        string tenantType,
        CancellationToken cancellationToken)
    {
        if (!GroupKindCodes.TryParse(tenantType, out var kind))
        {
            HeadKindMissing(_logger, headTenantId);
            return [];
        }

        return kind switch
        {
            GroupKind.Concesion => await _grants
                .ListEnabledOfficeIdsAsync(headTenantId, cancellationToken)
                .ConfigureAwait(false),
            GroupKind.MarcaBlanca => await ListMarcaBlancaEffectiveAsync(headTenantId, cancellationToken)
                .ConfigureAwait(false),
            _ => [],
        };
    }

    private async Task<IReadOnlyList<Guid>> ListMarcaBlancaEffectiveAsync(
        Guid headTenantId,
        CancellationToken cancellationToken)
    {
        var operable = await _operationalStatus.ListAsync(cancellationToken).ConfigureAwait(false);
        var ids = operable
            .Where(o => o.HasTenant && o.EstadoActivo == true)
            .Select(o => o.Id)
            .ToHashSet();

        var blocked = await _blocks.ListBlockedOfficeIdsAsync(headTenantId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var officeId in blocked)
        {
            ids.Remove(officeId);
        }

        return ids.OrderBy(id => id).ToList();
    }

    [LoggerMessage(
        EventId = 12347,
        Level = LogLevel.Warning,
        Message = "No se pudo resolver la cabeza {ParentTenantId} del tenant {TenantId}; lista efectiva vacía.")]
    private static partial void HierarchyResolutionFailed(ILogger logger, Guid tenantId, Guid parentTenantId);

    [LoggerMessage(
        EventId = 123471,
        Level = LogLevel.Warning,
        Message = "El tenant {TenantId} no tiene tipo de cabeza reconocido; lista efectiva vacía.")]
    private static partial void HeadKindMissing(ILogger logger, Guid tenantId);
}
