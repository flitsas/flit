namespace Flit.Admin.Application.Companies.Deeds.UpdateDeed;

/// <summary>
/// Cuerpo de la edición de una escritura (PUT). Actualiza descripción, vigencia y la multi-selección
/// de compañías. Para reemplazar el PDF, el cliente envía <c>Sha256</c> (hash hex del nuevo
/// documento); el backend devuelve una presigned POST policy nueva. Si <c>Sha256</c> es nulo/vacío,
/// se conserva el artefacto actual.
/// </summary>
public sealed record UpdateDeedRequest(
    string? Description,
    DateOnly VigenciaDesde,
    DateOnly VigenciaHasta,
    string? Sha256,
    IReadOnlyList<Guid>? RepresentedCompanyIds);
