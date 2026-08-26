namespace Flit.Admin.Application.Companies.Deeds.ListActiveDeeds;

/// <summary>
/// Escritura activa y VIGENTE de una compañía representada, proyectada para el collapse del primer
/// paso del wizard de trámites (HU #10903, ADR-0033 §5.4). Cada fila es el par (escritura × compañía
/// representada): una misma compañía (NIT) puede aparecer en VARIAS filas si tiene más de una
/// escritura vigente (Feature #10929), y <c>Id</c>/<c>Description</c> distinguen esas filas. <c>Nit</c>
/// es PII (Ley 1581): solo en respuestas autenticadas; no loguear.
/// <para>
/// Los campos <c>Representative*</c> identifican al RL que asoció la escritura (Feature #10929).
/// Quedan nulos en escrituras legadas sin <c>RepresentativeId</c>.
/// </para>
/// </summary>
public sealed record ActiveDeedResponse(
    Guid Id,
    string Nit,
    string Name,
    int DiasRestantes,
    DateOnly VigenciaHasta,
    string? Description,
    Guid? RepresentativeId = null,
    string? RepresentativeName = null,
    string? RepresentativeDocumentType = null,
    string? RepresentativeDocumentNumber = null);
