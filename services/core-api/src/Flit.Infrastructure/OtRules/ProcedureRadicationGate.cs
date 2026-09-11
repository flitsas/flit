using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Infrastructure.Persistence;
using Flit.Infrastructure.Persistence.Entities.Tramites;
using Flit.Tramites.Domain.Integration;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Segunda capa de defensa en la creación de trámite (HU #12348, #12409).
/// </summary>
internal sealed class ProcedureRadicationGate : IProcedureRadicationGate
{
    private readonly FlitDbContext _context;
    private readonly ICompanyHierarchyRepository _hierarchy;
    private readonly IEffectiveTransitOfficeListResolver _effectiveList;

    public ProcedureRadicationGate(
        FlitDbContext context,
        ICompanyHierarchyRepository hierarchy,
        IEffectiveTransitOfficeListResolver effectiveList)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _effectiveList = effectiveList ?? throw new ArgumentNullException(nameof(effectiveList));
    }

    public async Task<ProcedureRadicationGateResult> ValidateCreateAsync(
        Guid tenantId,
        Guid userId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default)
    {
        var activeCheck = await ValidateTenantActiveAsync(tenantId, userId, transitOfficeId, cancellationToken)
            .ConfigureAwait(false);

        if (!activeCheck.IsAllowed)
        {
            return activeCheck;
        }

        if (transitOfficeId is not { } otId || otId == Guid.Empty)
        {
            return new ProcedureRadicationGateResult(true, null);
        }

        var effective = await _effectiveList
            .ListEffectiveOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (effective.Contains(otId))
        {
            return new ProcedureRadicationGateResult(true, null);
        }

        await WriteDenialAsync(
            tenantId,
            userId,
            otId,
            ProcedureRadicationDenialReasons.OtNotPermitted,
            cancellationToken).ConfigureAwait(false);

        return new ProcedureRadicationGateResult(false, ProcedureRadicationDenialReasons.OtNotPermitted);
    }

    private async Task<ProcedureRadicationGateResult> ValidateTenantActiveAsync(
        Guid tenantId,
        Guid userId,
        Guid? transitOfficeId,
        CancellationToken cancellationToken)
    {
        var tenantActive = await _context.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == tenantId && t.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (!tenantActive)
        {
            await WriteDenialAsync(
                tenantId,
                userId,
                transitOfficeId,
                ProcedureRadicationDenialReasons.TenantInactive,
                cancellationToken).ConfigureAwait(false);

            return new ProcedureRadicationGateResult(false, ProcedureRadicationDenialReasons.TenantInactive);
        }

        var info = await _hierarchy.GetHierarchyInfoAsync(tenantId, cancellationToken).ConfigureAwait(false);
        if (info?.ParentTenantId is not { } parentId)
        {
            return new ProcedureRadicationGateResult(true, null);
        }

        var parentActive = await _context.Tenants
            .AsNoTracking()
            .AnyAsync(t => t.Id == parentId && t.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (parentActive)
        {
            return new ProcedureRadicationGateResult(true, null);
        }

        await WriteDenialAsync(
            tenantId,
            userId,
            transitOfficeId,
            ProcedureRadicationDenialReasons.NetworkInactive,
            cancellationToken).ConfigureAwait(false);

        return new ProcedureRadicationGateResult(false, ProcedureRadicationDenialReasons.NetworkInactive);
    }

    private async Task WriteDenialAsync(
        Guid tenantId,
        Guid userId,
        Guid? transitOfficeId,
        string reason,
        CancellationToken cancellationToken)
    {
        _context.ProcedureRadicationGateDenials.Add(new ProcedureRadicationGateDenial
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            TransitOfficeId = transitOfficeId,
            DenialReason = reason,
            OccurredAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
