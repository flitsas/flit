using Flit.Tramites.Domain.Enums;
using Flit.Tramites.Domain.Repositories;

namespace Flit.Tramites.Application.UseCases.ProcedureTypes;

/// <summary>
/// Resumen de un tipo publicado para el selector de operador (FEATURE-08 / CFD-12, §6.1).
/// Incluye <c>version</c> (a diferencia del listado SuperAdmin) para trazar la configuración vigente.
/// </summary>
public sealed record PublishedProcedureTypeDto(
    Guid Id,
    string Code,
    string Name,
    string Family,
    int Version,
    bool WizardEnabled);

/// <summary>
/// Lista los tipos de trámite en <c>publication_status='published'</c> disponibles para operadores
/// (CFD-12). Catálogo GLOBAL: no se filtra por tenant — el selector muestra todos los tipos publicados.
/// </summary>
public sealed class GetPublishedProcedureTypesHandler(IProcedureTypeRepository repository)
{
    public async Task<IReadOnlyList<PublishedProcedureTypeDto>> HandleAsync(CancellationToken ct = default)
    {
        var types = await repository.ListAsync(family: null, publicationStatus: PublicationStatus.Published, ct);
        return types
            .Select(t => new PublishedProcedureTypeDto(t.Id, t.Code, t.Name, t.Family, t.Version, t.WizardEnabled))
            .ToList();
    }
}
