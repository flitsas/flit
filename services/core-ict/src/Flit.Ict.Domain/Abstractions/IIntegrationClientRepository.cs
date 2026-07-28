using Flit.Ict.Domain.Entities;

namespace Flit.Ict.Domain.Abstractions;

/// <summary>Acceso a las credenciales de integración (<c>ict.integration_clients</c>).</summary>
public interface IIntegrationClientRepository
{
    /// <summary>Busca por username (global, sin RLS). Devuelve null si no existe.</summary>
    Task<IntegrationClient?> FindByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>Persiste cambios de intentos/lock/last_login tras un intento de login.</summary>
    Task SaveAsync(IntegrationClient client, CancellationToken ct = default);
}
