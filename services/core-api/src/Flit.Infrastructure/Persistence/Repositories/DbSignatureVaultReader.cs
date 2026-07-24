using Flit.Admin.Domain.Companies.SignatureVault;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Lecturas EF Core del baúl de firmas (HU #10642, ADR-0025). El baúl es tenant-scoped: las
/// lecturas corren bajo el contexto RLS del tenant fijando <c>app.current_tenant_id</c> con
/// <c>set_config(..., is_local := true)</c> (a diferencia del reader cross-tenant de mandatarios,
/// que corre con <c>row_security = off</c>). <c>DocumentNumber</c> es PII (Ley 1581): no loguear.
/// </summary>
internal sealed class DbSignatureVaultReader : ISignatureVaultReader
{
    private readonly FlitDbContext _context;

    public DbSignatureVaultReader(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public Task<SignatureVault?> FindActiveByNitAsync(
        Guid tenantId,
        string nitEmpresa,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nitEmpresa);
        var nit = nitEmpresa.Trim();

        return ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.SignatureVault
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId
                        && s.NitEmpresa == nit
                        && s.Estado == SignatureVaultEstadoMapping.Activa)
                    .OrderByDescending(s => s.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : SignatureVaultEstadoMapping.Rehydrate(entity);
            },
            cancellationToken);
    }

    public Task<IReadOnlyList<SignatureVaultItem>> ListByTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entities = await _context.SignatureVault
                    .AsNoTracking()
                    .Where(s => s.TenantId == tenantId)
                    // 'activa' < 'revocada' alfabéticamente → activas primero; luego creación desc.
                    .OrderBy(s => s.Estado)
                    .ThenByDescending(s => s.CreatedAt)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                IReadOnlyList<SignatureVaultItem> items =
                    [.. entities.Select(SignatureVaultEstadoMapping.ToItem)];
                return items;
            },
            cancellationToken);

    public Task<SignatureVaultItem?> GetByIdAsync(
        Guid tenantId,
        Guid id,
        CancellationToken cancellationToken = default) =>
        ExecuteInTenantScopeAsync(
            tenantId,
            async () =>
            {
                var entity = await _context.SignatureVault
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : SignatureVaultEstadoMapping.ToItem(entity);
            },
            cancellationToken);

    private async Task<T> ExecuteInTenantScopeAsync<T>(
        Guid tenantId,
        Func<Task<T>> read,
        CancellationToken cancellationToken)
    {
        if (!_context.Database.IsRelational())
        {
            return await read().ConfigureAwait(false);
        }

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

                var result = await read().ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return result;
            }
        }).ConfigureAwait(false);
    }
}
