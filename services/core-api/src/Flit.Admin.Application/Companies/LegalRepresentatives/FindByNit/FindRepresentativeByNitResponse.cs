namespace Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;

/// <summary>
/// Resultado del lookup de precarga por NIT (HU #10903, ADR-0033 §5.4). Si el tenant tiene un
/// representante activo para el NIT, el FE precarga comprador/vendedor con estos datos y NO consulta
/// RUNT/RUES. Las banderas <see cref="FirmaVigente"/>/<see cref="IdentidadVigente"/> se calculan al
/// momento (no son el estado congelado del guardado): el consumo reutiliza la firma del baúl o la
/// validación de identidad solo si SIGUEN vigentes. <c>Company.Nit</c> y <c>Representante.Documento</c>
/// son PII (Ley 1581): solo en respuestas autenticadas; no loguear.
/// </summary>
public sealed record FindRepresentativeByNitResponse(
    RepresentativeCompanyDto Company,
    RepresentativeContactDto Representante,
    bool FirmaVigente,
    bool IdentidadVigente);

/// <summary>Compañía representada precargada (razón social + contacto). <c>Nit</c> es PII.</summary>
public sealed record RepresentativeCompanyDto(
    string Nit,
    string RazonSocial,
    string? Email,
    string? Address,
    string? City,
    string? Phone);

/// <summary>Representante legal precargado. <c>Documento</c> es PII (@pii:high): no loguear.</summary>
public sealed record RepresentativeContactDto(
    string TipoDoc,
    string Documento,
    string Nombres,
    string PrimerApellido,
    string? SegundoApellido,
    string? Email,
    string? Telefono);
