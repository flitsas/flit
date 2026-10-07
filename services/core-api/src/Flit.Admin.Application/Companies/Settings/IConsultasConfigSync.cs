using Flit.Admin.Domain.Companies.Settings;

namespace Flit.Admin.Application.Companies.Settings;

/// <summary>
/// Lleva a Consultas la configuración de consultas que el SuperAdmin guarda en la pantalla de siempre (Epic #13316,
/// HU #13344; ADR-0065): cadena por tipo, presupuesto de failover, fuente de comparendos y avalúos. Solo existe con
/// <c>Consultas:Remoto:Habilitado</c>; sin ella no hay nada que sincronizar.
/// </summary>
public interface IConsultasConfigSync
{
    /// <exception cref="ConsultasConfigSyncException">Consultas no aceptó o no respondió.</exception>
    Task SyncAsync(Guid tenantId, TenantSettings settings, CancellationToken ct);
}

/// <summary>Consultas no aceptó la configuración o no respondió.</summary>
public sealed class ConsultasConfigSyncException : Exception
{
    public ConsultasConfigSyncException()
    {
    }

    public ConsultasConfigSyncException(string message)
        : base(message)
    {
    }

    public ConsultasConfigSyncException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
