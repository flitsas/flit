using System.Text.Json;
using Flit.Admin.Domain.Integrations;
using Flit.Infrastructure.Persistence.Entities.Integrations;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// HU #13084 — clientes de integración externos. Tabla de plataforma (sin tenant ni RLS): no hay
/// filtro de compañía que aplicar. Los hashes solo salen por <see cref="GetCredentialsByClientIdAsync"/>.
/// </summary>
internal sealed class ExternalClientRepository(FlitDbContext context) : IExternalClientRepository
{
    private const string ClientIdUniqueConstraint = "uq_external_clients_client_id";
    private const string CompanyTipoActivoUniqueIndex = "uq_external_clients_company_tipo_activo";

    public async Task<ExternalClientView> CreateAsync(NewExternalClient client, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        var entity = new ExternalClient
        {
            Id = Guid.CreateVersion7(),
            ClientId = client.ClientId,
            DisplayName = client.DisplayName,
            Purpose = client.Purpose,
            SecretHash = client.SecretHash,
            Scopes = JsonSerializer.Serialize(client.Scopes),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedBy = client.CreatedBy,
            Tipo = client.Tipo,
            CompanyId = client.CompanyId,
        };

        context.ExternalClients.Add(entity);
        try
        {
            await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ClientIdUniqueConstraint,
        })
        {
            context.Entry(entity).State = EntityState.Detached;
            throw new ExternalClientAlreadyExistsException(client.ClientId);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: CompanyTipoActivoUniqueIndex,
        })
        {
            context.Entry(entity).State = EntityState.Detached;
            throw new ExternalIntegrationAlreadyActiveException(client.CompanyId ?? Guid.Empty, client.Tipo);
        }

        return ToView(entity);
    }

    public async Task<ExternalClientView?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await context.ExternalClients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToView(entity);
    }

    public async Task<ExternalClientCredentials?> GetCredentialsByClientIdAsync(
        string clientId, CancellationToken cancellationToken = default)
    {
        var entity = await context.ExternalClients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ClientId == clientId && c.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);

        return entity is null
            ? null
            : new ExternalClientCredentials(
                entity.Id,
                entity.ClientId,
                entity.SecretHash,
                entity.PreviousSecretHash,
                entity.SecretRotatedAt,
                entity.MustRotate,
                entity.IsActive,
                ReadScopes(entity.Scopes),
                entity.FailedAttempts,
                entity.LockedUntil,
                entity.Tipo);
    }

    public async Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Sin paginar: son unos pocos clientes de máquina (uno por sistema y ambiente).
        var entities = await context.ExternalClients.AsNoTracking()
            .Where(c => c.DeletedAt == null && c.Tipo == ExternalIntegrationTypes.Externa)
            .OrderBy(c => c.ClientId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(ToView).ToList();
    }

    public async Task<DateTimeOffset?> RegisterFailedAttemptAsync(
        Guid id, int maxFailedAttempts, TimeSpan lockDuration, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        // Una sola sentencia: el incremento lo hace la base, así dos fallos simultáneos cuentan dos.
        var lockUntil = now.Add(lockDuration);
        var result = await context.Database.SqlQuery<DateTimeOffset?>($"""
            UPDATE integrations.external_clients
               SET failed_attempts = CASE WHEN failed_attempts + 1 >= {maxFailedAttempts} THEN 0 ELSE failed_attempts + 1 END,
                   locked_until    = CASE WHEN failed_attempts + 1 >= {maxFailedAttempts} THEN {lockUntil} ELSE locked_until END
             WHERE id = {id}
            RETURNING locked_until AS "Value"
            """)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return result.Count == 0 || result[0] is not { } until || until <= now ? null : until;
    }

    public async Task RegisterTokenIssuedAsync(Guid id, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlAsync($"""
            UPDATE integrations.external_clients
               SET failed_attempts = 0, locked_until = NULL, last_token_at = {now}
             WHERE id = {id}
            """, cancellationToken)
            .ConfigureAwait(false);
    }

    // HU #13088 — las escrituras de administración son un UPDATE directo (ExecuteUpdate), no una entidad
    // rastreada: el login cuenta fallos con su propio UPDATE y el row_version (token de concurrencia) cambiaría
    // entre la lectura y el guardado. Aquí gana la última escritura, que es lo esperado en un cambio de admin.
    public async Task<ExternalClientView?> UpdateAsync(
        Guid id, ExternalClientChanges changes, Guid? actor, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var scopesJson = changes.Scopes is null ? null : JsonSerializer.Serialize(changes.Scopes);

        var rows = await Vigentes(id).ExecuteUpdateAsync(s =>
        {
            if (changes.DisplayName is not null)
            {
                s.SetProperty(c => c.DisplayName, changes.DisplayName);
            }

            if (changes.Purpose is not null)
            {
                s.SetProperty(c => c.Purpose, changes.Purpose);
            }

            if (scopesJson is not null)
            {
                s.SetProperty(c => c.Scopes, scopesJson);
            }

            if (changes.IsActive is { } isActive)
            {
                s.SetProperty(c => c.IsActive, isActive);
            }

            if (changes.MustRotate is { } mustRotate)
            {
                s.SetProperty(c => c.MustRotate, mustRotate);
            }

            s.SetProperty(c => c.UpdatedAt, now);
            s.SetProperty(c => c.UpdatedBy, actor);
        }, cancellationToken).ConfigureAwait(false);

        return rows == 0 ? null : await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalClientView?> ReplaceSecretAsync(
        Guid id, string newSecretHash, bool revokePrevious, Guid? actor, DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newSecretHash);

        var rows = await Vigentes(id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.PreviousSecretHash, c => revokePrevious ? null : c.SecretHash)
            .SetProperty(c => c.SecretHash, newSecretHash)
            .SetProperty(c => c.SecretRotatedAt, now)
            .SetProperty(c => c.MustRotate, false)
            .SetProperty(c => c.FailedAttempts, 0)
            .SetProperty(c => c.LockedUntil, (DateTimeOffset?)null)
            .SetProperty(c => c.UpdatedAt, now)
            .SetProperty(c => c.UpdatedBy, actor), cancellationToken).ConfigureAwait(false);

        return rows == 0 ? null : await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<ExternalClientView?> UnlockAsync(
        Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var rows = await Vigentes(id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.FailedAttempts, 0)
            .SetProperty(c => c.LockedUntil, (DateTimeOffset?)null)
            .SetProperty(c => c.UpdatedAt, now)
            .SetProperty(c => c.UpdatedBy, actor), cancellationToken).ConfigureAwait(false);

        return rows == 0 ? null : await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    // HU #14067 — integraciones ligadas a una compañía.
    public Task<bool> CompanyExistsAsync(Guid companyId, CancellationToken cancellationToken = default) =>
        context.Tenants.AsNoTracking().AnyAsync(t => t.Id == companyId, cancellationToken);

    public async Task<IReadOnlyList<ExternalClientView>> ListByCompanyAsync(
        Guid companyId, CancellationToken cancellationToken = default)
    {
        var entities = await context.ExternalClients.AsNoTracking()
            .Where(c => c.CompanyId == companyId && c.DeletedAt == null)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(ToView).ToList();
    }

    public async Task<ExternalClientView?> GetByCompanyAsync(
        Guid companyId, Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await context.ExternalClients.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id && c.CompanyId == companyId && c.DeletedAt == null, cancellationToken)
            .ConfigureAwait(false);
        return entity is null ? null : ToView(entity);
    }

    public async Task<ExternalClientView?> BlockAsync(
        Guid id, Guid? actor, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var rows = await Vigentes(id).ExecuteUpdateAsync(s => s
            .SetProperty(c => c.LockedUntil, ExternalIntegrationRules.IndefiniteBlock)
            .SetProperty(c => c.UpdatedAt, now)
            .SetProperty(c => c.UpdatedBy, actor), cancellationToken).ConfigureAwait(false);

        return rows == 0 ? null : await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
    }

    private IQueryable<ExternalClient> Vigentes(Guid id) =>
        context.ExternalClients.Where(c => c.Id == id && c.DeletedAt == null);

    private static ExternalClientView ToView(ExternalClient entity) => new(
        entity.Id,
        entity.ClientId,
        entity.DisplayName,
        entity.Purpose,
        ReadScopes(entity.Scopes),
        entity.IsActive,
        entity.MustRotate,
        entity.LockedUntil,
        entity.LastTokenAt,
        entity.CreatedAt,
        entity.Tipo,
        entity.CompanyId);

    private static List<string> ReadScopes(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
