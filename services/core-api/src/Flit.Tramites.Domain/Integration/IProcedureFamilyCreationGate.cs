namespace Flit.Tramites.Domain.Integration;

/// <summary>
/// Gate de creación por familia de trámite: si la compañía tiene bloqueada la familia,
/// no se puede crear una instancia de ese tipo.
/// </summary>
public interface IProcedureFamilyCreationGate
{
    /// <summary>
    /// True si la familia está bloqueada para el tenant (no se permite crear).
    /// Familia desconocida → no bloquea.
    /// </summary>
    Task<bool> IsFamilyBlockedAsync(Guid tenantId, string? procedureFamily, CancellationToken ct = default);
}

/// <summary>Null-object: no bloquea ninguna familia (tests / entornos sin Admin).</summary>
public sealed class NullProcedureFamilyCreationGate : IProcedureFamilyCreationGate
{
    public Task<bool> IsFamilyBlockedAsync(Guid tenantId, string? procedureFamily, CancellationToken ct = default)
        => Task.FromResult(false);
}
