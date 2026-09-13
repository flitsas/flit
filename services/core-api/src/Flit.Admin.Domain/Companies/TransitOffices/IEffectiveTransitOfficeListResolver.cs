namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Resuelve la lista efectiva de OT habilitados para un tenant según jerarquía (HU #12347).
/// </summary>
public interface IEffectiveTransitOfficeListResolver
{
    Task<IReadOnlyList<Guid>> ListEffectiveOfficeIdsAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default);
}
