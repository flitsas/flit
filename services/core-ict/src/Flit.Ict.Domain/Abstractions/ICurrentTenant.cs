namespace Flit.Ict.Domain.Abstractions;

/// <summary>
/// Contexto del cliente de integración autenticado (resuelto del token ICT). Impone el tenant en el
/// pipeline (RLS): el cliente nunca elige tenant por payload/header.
/// </summary>
public interface ICurrentTenant
{
    /// <summary>Tenant del token, o null si la petición no está autenticada como cliente ICT.</summary>
    Guid? TenantId { get; }

    /// <summary>NIT del tenant (para validar company_manager_document del payload).</summary>
    string? CompanyNit { get; }

    /// <summary>Id del cliente de integración (sub del token) usado como created_by.</summary>
    Guid? IntegrationClientId { get; }
}
