namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Resuelve la lista efectiva de OT habilitados para un tenant según jerarquía (HU #12347).
/// </summary>
public interface IEffectiveTransitOfficeListResolver
{
    Task<IReadOnlyList<Guid>> ListEffectiveOfficeIdsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cálculo inverso (Bug #12912): tenants cuya lista efectiva contiene
    /// <paramref name="transitOfficeId"/>. Coherente por definición con
    /// <see cref="ListEffectiveOfficeIdsAsync"/>: <c>officeId ∈ Effective(t) ⇔ t ∈ Inverse(officeId)</c>.
    /// Lo usan los consumidores con eje OT (mandatarios por organismo, alcance de reportes OT).
    /// </summary>
    Task<IReadOnlyList<Guid>> ListEffectiveTenantIdsForOfficeAsync(
        Guid transitOfficeId,
        CancellationToken cancellationToken = default);
}
