using Flit.Admin.Domain.OtProfile;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio del perfil OT (HU #10215). RLS vía <c>app.current_tenant_id</c>
/// en proveedor relacional; en InMemory filtra por tenant_id explícito.
/// </summary>
internal sealed class OtProfileRepository : IOtProfileRepository
{
    /// <summary>OT por defecto para bootstrap cuando no hay grant (tests / dev).</summary>
    internal static readonly Guid DefaultTransitOfficeId =
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly FlitDbContext _context;

    public OtProfileRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<OtProfile?> GetByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var profile = await ExecuteInTenantScopeAsync(
            tenantId,
            async () => await _context.TransitOfficeProfiles
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (profile is null)
        {
            return null;
        }

        var flags = await ExecuteInTenantScopeAsync(
            tenantId,
            async () => await _context.OtFeatureFlags
                .AsNoTracking()
                .Where(f => f.TenantId == tenantId)
                .OrderBy(f => f.FlagKey)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        return Map(profile, flags);
    }

    public Task<OtProfile> SaveAsync(
        Guid tenantId,
        string operationMode,
        bool quipuxReadOnly,
        Guid? changedBy,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var now = DateTimeOffset.UtcNow;
                var entity = await _context.TransitOfficeProfiles
                    .FirstOrDefaultAsync(p => p.TenantId == tenantId, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    entity = new TransitOfficeProfile
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        TransitOfficeId = await ResolveTransitOfficeIdAsync(tenantId, cancellationToken)
                            .ConfigureAwait(false),
                        CreatedAt = now,
                        CreatedBy = changedBy,
                    };
                    _context.TransitOfficeProfiles.Add(entity);
                }
                else
                {
                    entity.UpdatedAt = now;
                    entity.UpdatedBy = changedBy;
                }

                entity.OperationMode = operationMode;
                entity.QuipuxReadOnly = quipuxReadOnly;

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                var flags = await _context.OtFeatureFlags
                    .AsNoTracking()
                    .Where(f => f.TenantId == tenantId)
                    .OrderBy(f => f.FlagKey)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return Map(entity, flags);
            },
            cancellationToken);

    private async Task<Guid> ResolveTransitOfficeIdAsync(
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var grantOfficeId = await _context.TenantTransitOfficeGrants
            .AsNoTracking()
            .Where(g => g.TenantId == tenantId && g.IsEnabled)
            .OrderBy(g => g.CreatedAt)
            .Select(g => g.TransitOfficeId)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return grantOfficeId == Guid.Empty ? DefaultTransitOfficeId : grantOfficeId;
    }

    private static OtProfile Map(TransitOfficeProfile entity, IReadOnlyList<OtFeatureFlagEntity> flags) =>
        new()
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            TransitOfficeId = entity.TransitOfficeId,
            OperationMode = entity.OperationMode,
            QuipuxReadOnly = entity.QuipuxReadOnly,
            FeatureFlags = flags.Select(f => new OtFeatureFlag
            {
                Id = f.Id,
                TenantId = f.TenantId,
                FlagKey = f.FlagKey,
                IsEnabled = f.IsEnabled,
                ConfigJson = f.Config,
            }).ToList(),
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
