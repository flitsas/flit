using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>Acceso EF a <c>ict.integration_clients</c> (tabla sin RLS: búsqueda global por username).</summary>
public sealed class IntegrationClientRepository(IctDbContext db) : IIntegrationClientRepository
{
    public async Task<IntegrationClient?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        // Case-insensitive por normalización en la app (el username se guarda en minúsculas), no por citext.
        var normalized = (username ?? string.Empty).Trim().ToLowerInvariant();
        return await db.IntegrationClients
            .FirstOrDefaultAsync(c => c.Username == normalized && c.DeletedAt == null, ct);
    }

    public async Task<IntegrationClient?> FindByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await db.IntegrationClients
            .FirstOrDefaultAsync(c => c.Id == id && c.DeletedAt == null, ct);
    }

    public async Task<IReadOnlyList<IntegrationClient>> ListAsync(Guid? tenantId, CancellationToken ct = default)
    {
        var query = db.IntegrationClients.Where(c => c.DeletedAt == null);
        if (tenantId is { } t)
        {
            query = query.Where(c => c.TenantId == t);
        }

        return await query.OrderBy(c => c.Username).ToListAsync(ct);
    }

    public async Task AddAsync(IntegrationClient client, CancellationToken ct = default)
    {
        db.IntegrationClients.Add(client);
        await db.SaveChangesAsync(ct);
    }

    public async Task SaveAsync(IntegrationClient client, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }
}
