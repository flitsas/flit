namespace Flit.Tramites.Domain.Integration;

/// <summary>OT del catálogo FLIT resuelto para vincular al trámite (id + code + name + city).</summary>
public sealed record ResolvedTransitOffice(Guid Id, string Code, string Name, string? CityCode);

/// <summary>
/// Puerto para resolver un organismo de tránsito HABILITADO de la empresa a partir del nombre que
/// devuelve el RUNT (B11, HU #10659). En traspaso el OT ya viene fijado por el RUNT: tras hidratar
/// <c>transit_office_name</c> en el preflight, se resuelve contra los OT habilitados de la empresa
/// (grants + catálogo) para poblar también <c>transit_office_id</c>. Desacopla el módulo de trámites
/// del de Admin (mismo patrón que <see cref="IRnmcRequirementPolicy"/>). El match es por nombre,
/// case-insensitive (misma regla que <c>runtSuggestion</c> en el frontend).
/// </summary>
public interface ITransitOfficeResolver
{
    /// <summary>
    /// Devuelve el OT habilitado cuyo nombre coincide (case-insensitive) con
    /// <paramref name="transitOfficeName"/>, o <c>null</c> si el nombre RUNT no corresponde a ningún
    /// OT habilitado de la empresa (en cuyo caso NO se debe inventar un <c>transit_office_id</c>).
    /// </summary>
    Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
        Guid tenantId,
        string transitOfficeName,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Implementación que NUNCA resuelve un OT — default seguro para tests de dominio/aplicación que no
/// ejercitan la vinculación contra el catálogo de la empresa.
/// </summary>
public sealed class NullTransitOfficeResolver : ITransitOfficeResolver
{
    public static NullTransitOfficeResolver Instance { get; } = new();

    public Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
        Guid tenantId,
        string transitOfficeName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<ResolvedTransitOffice?>(null);
}
