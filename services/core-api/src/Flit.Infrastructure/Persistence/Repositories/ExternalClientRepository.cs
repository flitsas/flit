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
                entity.LockedUntil);
    }

    public async Task<IReadOnlyList<ExternalClientView>> ListAsync(CancellationToken cancellationToken = default)
    {
        // Sin paginar: son unos pocos clientes de máquina (uno por sistema y ambiente).
        var entities = await context.ExternalClients.AsNoTracking()
            .Where(c => c.DeletedAt == null)
            .OrderBy(c => c.ClientId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        return entities.Select(ToView).ToList();
    }

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
        entity.CreatedAt);

    private static List<string> ReadScopes(string json) =>
        JsonSerializer.Deserialize<List<string>>(json) ?? [];
}
