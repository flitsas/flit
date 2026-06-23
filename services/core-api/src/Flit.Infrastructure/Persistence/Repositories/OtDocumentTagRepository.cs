using Flit.Admin.Domain.OtDocumentTags;
using Flit.Infrastructure.Persistence.Entities.Admin;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>Etiquetas documentales OT (HU #10222).</summary>
internal sealed class OtDocumentTagRepository : IOtDocumentTagRepository
{
    private readonly FlitDbContext _context;

    public OtDocumentTagRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<IReadOnlyList<OtDocumentTag>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entities = await _context.OtDocumentTags
                    .AsNoTracking()
                    .Where(t => t.TenantId == tenantId)
                    .OrderBy(t => t.Code)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entities.Select(Map).ToList();
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<bool> ExistsCodeAsync(
        Guid tenantId,
        string code,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () => await _context.OtDocumentTags
                .AsNoTracking()
                .AnyAsync(t => t.TenantId == tenantId && t.Code == code, cancellationToken)
                .ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

    public async Task<OtDocumentTag> CreateAsync(
        Guid tenantId,
        string code,
        string name,
        string color,
        Guid? createdBy,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = new OtDocumentTagEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    Code = code,
                    Name = name,
                    Color = color,
                    CreatedAt = DateTimeOffset.UtcNow,
                    CreatedBy = createdBy,
                };

                _context.OtDocumentTags.Add(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Map(entity);
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<bool> DeleteAsync(
        Guid tenantId,
        Guid tagId,
        CancellationToken cancellationToken = default) =>
        await ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.OtDocumentTags
                    .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Id == tagId, cancellationToken)
                    .ConfigureAwait(false);

                if (entity is null)
                {
                    return false;
                }

                _context.OtDocumentTags.Remove(entity);
                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken).ConfigureAwait(false);

    private static OtDocumentTag Map(OtDocumentTagEntity entity) => new()
    {
        Id = entity.Id,
        TenantId = entity.TenantId,
        Code = entity.Code,
        Name = entity.Name,
        Color = entity.Color,
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
