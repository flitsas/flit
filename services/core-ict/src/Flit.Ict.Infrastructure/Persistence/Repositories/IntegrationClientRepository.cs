using Flit.Ict.Domain.Abstractions;
using Flit.Ict.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flit.Ict.Infrastructure.Persistence.Repositories;

/// <summary>Acceso EF a <c>ict.integration_clients</c> (tabla sin RLS: búsqueda global por username).</summary>
public sealed class IntegrationClientRepository(IctDbContext db) : IIntegrationClientRepository
{
    public async Task<IntegrationClient?> FindByUsernameAsync(string username, CancellationToken ct = default)
    {
        // La columna es citext → la comparación es case-insensitive en el motor.
        return await db.IntegrationClients
            .FirstOrDefaultAsync(c => c.Username == username && c.DeletedAt == null, ct);
    }

    public async Task SaveAsync(IntegrationClient client, CancellationToken ct = default)
    {
        await db.SaveChangesAsync(ct);
    }
}
