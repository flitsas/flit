using Flit.Admin.Domain.OtRequirements;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de requisitos por OT (HU #10545). RLS vía <c>app.current_tenant_id</c> en el
/// proveedor relacional; en InMemory filtra por <c>tenant_id</c> explícito. Mismo patrón que
/// <see cref="OtProfileRepository"/>.
/// </summary>
internal sealed class OtRequirementsRepository : IOtRequirementsRepository
{
    private readonly FlitDbContext _context;

    public OtRequirementsRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<OtRequirements?> GetByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var entity = await ExecuteInTenantScopeAsync(
            tenantId,
            async () => await _context.OtRequirements
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return entity is null ? null : Map(entity);
    }

    public Task<OtRequirements> SaveAsync(
        Guid tenantId,
        bool requiresRnmc,
        bool allowPlatePreassign,
        bool identityValidationEnabled,
        Guid? changedBy,
        Guid? transitOfficeId = null,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var entity = await _context.OtRequirements
                    .FirstOrDefaultAsync(r => r.TenantId == tenantId, cancellationToken)
                    .ConfigureAwait(false);

                var resolvedOfficeId = await ResolveTransitOfficeIdAsync(
                    tenantId,
                    transitOfficeId,
                    cancellationToken).ConfigureAwait(false);

                if (entity is null)
                {
                    // La constraint uq_ot_requirements_transit_office_id es global (una fila por
                    // oficina). Si la oficina resuelta ya la configuró otro tenant, un INSERT
                    // reventaría con 23505 (500 crudo). Lo detectamos y lo reportamos limpio.
                    var takenByOther = await _context.OtRequirements
                        .AsNoTracking()
                        .AnyAsync(
                            r => r.TransitOfficeId == resolvedOfficeId && r.TenantId != tenantId,
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (takenByOther)
                    {
                        throw new OtRequirementsScopeException(
                            $"La oficina {resolvedOfficeId} ya tiene requisitos configurados por otro tenant; " +
                            "el tenant actual no es su organismo de tránsito.");
                    }

                    entity = new OtRequirementsEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        TransitOfficeId = resolvedOfficeId,
                        CreatedAt = now,
                        CreatedBy = changedBy,
                    };
                    _context.OtRequirements.Add(entity);
                }
                else
                {
                    entity.UpdatedAt = now;
                    entity.UpdatedBy = changedBy;
                    if (transitOfficeId is Guid explicitOfficeId && explicitOfficeId != Guid.Empty)
                    {
                        entity.TransitOfficeId = explicitOfficeId;
                    }
                }

                entity.RequiresRnmc = requiresRnmc;
                entity.AllowPlatePreassign = allowPlatePreassign;
                entity.IdentityValidationEnabled = identityValidationEnabled;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                return Map(entity);
            },
            cancellationToken);

    private async Task<Guid> ResolveTransitOfficeIdAsync(
        Guid tenantId,
        Guid? explicitTransitOfficeId,
        CancellationToken cancellationToken)
    {
        if (explicitTransitOfficeId is Guid explicitId && explicitId != Guid.Empty)
        {
            return explicitId;
        }

        // El OT es tenant de primera clase: su fila en transit_office_profiles fija el office id.
        var profileOfficeId = await _context.TransitOfficeProfiles
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .Select(p => p.TransitOfficeId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (profileOfficeId != Guid.Empty)
        {
            return profileOfficeId;
        }

        var grantOfficeId = await _context.TenantTransitOfficeGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.IsEnabled)
            .OrderBy(g => g.CreatedAt)
            .Select(g => g.TransitOfficeId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (grantOfficeId != Guid.Empty)
        {
            return grantOfficeId;
        }

        // Sin perfil ni grant el tenant NO es un OT aprovisionado. Antes se caía al OT por defecto
        // (aaaaaaaa-0001), lo que permitía que una empresa (tenant_type FLIT) ocupara la fila de
        // requisitos del OT dueño de esa oficina y bloqueara al OT real con un 23505. La escritura
        // exige una oficina propia; el bootstrap de dev/tests debe sembrar el perfil del OT primero.
        throw new OtRequirementsScopeException(
            $"El tenant {tenantId} no está aprovisionado como organismo de tránsito " +
            "(sin perfil ni grant); no puede configurar requisitos de OT.");
    }

    private static OtRequirements Map(OtRequirementsEntity entity) => new()
    {
        TransitOfficeId = entity.TransitOfficeId,
        RequiresRnmc = entity.RequiresRnmc,
        AllowPlatePreassign = entity.AllowPlatePreassign,
        IdentityValidationEnabled = entity.IdentityValidationEnabled,
    };

    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (_context.Database.IsRelational())
        {
            var strategy = _context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                var transaction = await _context.Database
                    .BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

                await using (transaction.ConfigureAwait(false))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"SELECT set_config('app.current_tenant_id', {tenantId.ToString()}, true)",
                        cancellationToken).ConfigureAwait(false);

                    var result = await action().ConfigureAwait(false);
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                    return result;
                }
            }).ConfigureAwait(false);
        }

        return await action().ConfigureAwait(false);
    }
}
