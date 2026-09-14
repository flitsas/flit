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
