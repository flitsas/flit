using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Domain.Abstractions;

/// <summary>Acceso a las credenciales de integración (<c>ict.integration_clients</c>).</summary>
public interface IIntegrationClientRepository
{
    /// <summary>Busca por username (global, sin RLS). Devuelve null si no existe.</summary>
    Task<IntegrationClient?> FindByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>Busca por id (no borrado). Devuelve null si no existe.</summary>
    Task<IntegrationClient?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Lista los clientes (no borrados), opcionalmente filtrados por tenant, orden por username.</summary>
    Task<IReadOnlyList<IntegrationClient>> ListAsync(Guid? tenantId, CancellationToken ct = default);

    /// <summary>Agrega un cliente nuevo y persiste.</summary>
    Task AddAsync(IntegrationClient client, CancellationToken ct = default);

    /// <summary>Persiste cambios de intentos/lock/last_login tras un intento de login o edición admin.</summary>
    Task SaveAsync(IntegrationClient client, CancellationToken ct = default);
}
