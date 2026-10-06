namespace Flit.Tramites.Application.UseCases.Avaluos;

/// <summary>
/// Resuelve qué proveedores de avalúo están habilitados para un tenant y cuál es el sugerido
/// (Feature #10707). Vive como puerto en Application; la implementación (que lee la configuración
/// operativa del tenant) reside en Infraestructura, cruzando el límite Admin↔Trámites — igual que
/// <c>IConsultationTenantOverrideProvider</c> para las consultas RUNT.
/// </summary>
public interface IAvaluoProviderPolicy
{
    Task<AvaluoEnabledSet> GetAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>Conjunto de proveedores de avalúo habilitados + el sugerido por defecto.</summary>
public sealed record AvaluoEnabledSet(IReadOnlyList<string> Enabled, string Primary)
{
    /// <summary>Default sin configuración por tenant: solo Fasecolda.</summary>
    public static AvaluoEnabledSet Default { get; } = new(["fasecolda"], "fasecolda");
}
