using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Tramites.Domain.Integration;

namespace Flit.Infrastructure.OtRules;

/// <summary>
/// Implementación del puerto <see cref="ITransitOfficeResolver"/> (B11, HU #10659): resuelve el OT
/// habilitado de la empresa por su nombre RUNT usando la MISMA lógica que
/// <c>GET /api/v1/tramites/transit-offices</c> — los grants vigentes del tenant
/// (<see cref="ITransitGrantRepository"/>) resueltos contra el catálogo
/// (<see cref="ITransitOfficeCatalog"/>). El match de nombre es case-insensitive (paridad con
/// <c>runtSuggestion</c> del frontend). Clon del patrón de <c>RnmcRequirementPolicy</c>.
/// </summary>
internal sealed class TransitOfficeResolver : ITransitOfficeResolver
{
    private readonly ITransitGrantRepository _grants;
    private readonly ITransitOfficeCatalog _catalog;

    public TransitOfficeResolver(ITransitGrantRepository grants, ITransitOfficeCatalog catalog)
    {
        _grants = grants ?? throw new ArgumentNullException(nameof(grants));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<ResolvedTransitOffice?> ResolveEnabledByNameAsync(
        Guid tenantId,
        string transitOfficeName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transitOfficeName))
        {
            return null;
        }

        var enabledIds = await _grants
            .ListEnabledOfficeIdsAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        var name = transitOfficeName.Trim();
        var match = enabledIds
            .Select(_catalog.GetById)
            .FirstOrDefault(o => o is not null
                && string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase));

        return match is null
            ? null
            : new ResolvedTransitOffice(match.Id, match.Code, match.Name, match.CityCode);
    }
}
