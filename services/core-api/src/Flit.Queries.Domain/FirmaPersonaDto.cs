namespace Flit.Queries.Domain;

/// <summary>
/// Firma de UNA persona que debe firmar un trámite (Consultas del gestor y del organismo). <see cref="Rol"/> es
/// «Vendedor» o «Comprador»; <see cref="Estado"/> es el texto legible («Firmó», «Pendiente», «Rechazada», «Expirada»,
/// «Sin validar»); <see cref="FechaFirma"/> solo si existe una validación aprobada propia.
/// </summary>
public sealed record FirmaPersonaDto(
    string Rol,
    int Ordinal,
    string Nombre,
    string? TipoDocumento,
    string? Documento,
    bool Firmo,
    string Estado,
    DateTimeOffset? FechaFirma);
