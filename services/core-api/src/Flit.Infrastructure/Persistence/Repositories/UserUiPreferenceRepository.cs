using Flit.Infrastructure.Persistence.Entities.Admin;
using Flit.Modules.Security.Domain.UiPreferences;
using Microsoft.EntityFrameworkCore;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositorio de preferencias de UI por usuario (<c>admin.user_ui_preferences</c>). Cambios
/// auditados vía trigger <c>tr_user_ui_preferences_audit</c> en PostgreSQL (mismo patrón que
/// <see cref="OtFeatureFlagRepository"/>).
/// </summary>
internal sealed class UserUiPreferenceRepository : IUserUiPreferenceRepository
{
    private readonly FlitDbContext _context;

    public UserUiPreferenceRepository(FlitDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<UserUiPreference?> FindAsync(
        Guid tenantId,
        Guid userId,
        string scope,
        CancellationToken cancellationToken = default) =>
        await TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.UserUiPreferences
                    .AsNoTracking()
                    .FirstOrDefaultAsync(
                        p => p.TenantId == tenantId && p.UserId == userId && p.Scope == scope,
                        cancellationToken)
                    .ConfigureAwait(false);

                return entity is null ? null : Map(entity);
            },
            cancellationToken).ConfigureAwait(false);

    public async Task<UserUiPreference> UpsertAsync(
        Guid tenantId,
        Guid userId,
        string scope,
        string valueJson,
        CancellationToken cancellationToken = default) =>
        await TenantRlsScope.ExecuteAsync(
            _context,
            tenantId,
            async () =>
            {
                var entity = await _context.UserUiPreferences
                    .FirstOrDefaultAsync(
                        p => p.TenantId == tenantId && p.UserId == userId && p.Scope == scope,
                        cancellationToken)
                    .ConfigureAwait(false);

                var now = DateTimeOffset.UtcNow;
                if (entity is null)
                {
                    // Fila nueva: created_at la fija la BD (default now()); acá basta con no
                    // tocarla para que EF no la sobrescriba con el default de DateTimeOffset.
                    entity = new UserUiPreferenceEntity
                    {
                        Id = Guid.NewGuid(),
                        TenantId = tenantId,
                        UserId = userId,
                        Scope = scope,
                        Value = valueJson,
                        CreatedAt = now,
                    };
                    _context.UserUiPreferences.Add(entity);
                }
                else
                {
                    // Upsert idempotente: sobrescribe el value existente, nunca duplica (la
                    // unicidad tenant+usuario+scope es la que garantiza este camino "update").
                    entity.Value = valueJson;
                    entity.UpdatedAt = now;
                }

                await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                return Map(entity);
            },
            cancellationToken).ConfigureAwait(false);

    private static UserUiPreference Map(UserUiPreferenceEntity entity) => new()
    {
        TenantId = entity.TenantId,
        UserId = entity.UserId,
        Scope = entity.Scope,
        ValueJson = entity.Value,
    };
}
