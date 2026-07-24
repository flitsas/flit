namespace Flit.Tramites.Application.UseCases.ProcedureInstances;

/// <summary>
/// Política del feature flag <c>F08_DynamicProcedures</c> (FEATURE-08 / CFD-09/CFD-10). Indica si el
/// wizard dinámico está habilitado para el tenant. Mismo patrón que las demás políticas del wizard:
/// puerto en Application con null-object por defecto (deshabilitado) y binding real en Infraestructura.
/// </summary>
public interface IDynamicProceduresPolicy
{
    Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default);
}

/// <summary>
/// Implementación por defecto (deshabilitado): el wizard usa el camino estático
/// (BuildMatricula/BuildTraspaso). Preserva el comportamiento previo en tests y en tenants sin el flag.
/// </summary>
public sealed class NullDynamicProceduresPolicy : IDynamicProceduresPolicy
{
    public static NullDynamicProceduresPolicy Instance { get; } = new();

    public Task<bool> IsEnabledAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
