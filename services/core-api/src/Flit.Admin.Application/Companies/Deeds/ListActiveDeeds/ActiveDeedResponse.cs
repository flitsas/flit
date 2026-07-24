namespace Flit.Admin.Application.Companies.Deeds.ListActiveDeeds;

/// <summary>
/// Escritura activa y VIGENTE de una compañía representada, proyectada para el collapse del primer
/// paso del wizard de trámites (HU #10903, ADR-0033 §5.4). Cada fila es una compañía (NIT) cubierta
/// por una escritura vigente del tenant. <c>Nit</c> es PII (Ley 1581): solo en respuestas
/// autenticadas; no loguear.
/// </summary>
public sealed record ActiveDeedResponse(
    string Nit,
    string Name,
    int DiasRestantes,
    DateOnly VigenciaHasta);
