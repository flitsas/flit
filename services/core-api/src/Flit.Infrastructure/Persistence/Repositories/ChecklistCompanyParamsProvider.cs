using Flit.Admin.Domain.CompanyDocumentParams;
using Flit.Tramites.Domain.Repositories;
using Flit.Tramites.Domain.Tramites.ValueObjects;

namespace Flit.Infrastructure.Persistence.Repositories;

/// <summary>
/// Puente que expone los parámetros documentales por gestora (RF31) al motor de checklist de
/// trámites, mapeando la persistencia Admin (<see cref="ICompanyDocumentParamRepository"/>,
/// estado como texto <c>OCULTO|OBLIGATORIO|OPCIONAL</c>) a los VO del dominio de trámites
/// (<see cref="CompanyDocumentParam"/>). Los estados no reconocidos se ignoran.
/// </summary>
internal sealed class ChecklistCompanyParamsProvider(ICompanyDocumentParamRepository repository)
    : IChecklistCompanyParamsProvider
{
    public async Task<IReadOnlyList<CompanyDocumentParam>> GetForTenantAsync(
        Guid tenantId,
        CancellationToken cancellationToken = default)
    {
        var items = await repository.ListByTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);

        var result = new List<CompanyDocumentParam>(items.Count);
        foreach (var item in items)
        {
            if (TryMapState(item.State, out var state))
            {
                result.Add(new CompanyDocumentParam(item.DocumentTypeCode, state));
            }
        }

        return result;
    }

    private static bool TryMapState(string? raw, out CompanyDocumentParamState state)
    {
        switch (raw)
        {
            case CompanyDocumentParamStates.Oculto:
                state = CompanyDocumentParamState.Oculto;
                return true;
            case CompanyDocumentParamStates.Obligatorio:
                state = CompanyDocumentParamState.Obligatorio;
                return true;
            case CompanyDocumentParamStates.Opcional:
                state = CompanyDocumentParamState.Opcional;
                return true;
            default:
                state = default;
                return false;
        }
    }
}
