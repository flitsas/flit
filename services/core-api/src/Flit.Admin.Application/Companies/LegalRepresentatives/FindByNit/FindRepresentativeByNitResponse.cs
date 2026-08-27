namespace Flit.Admin.Application.Companies.LegalRepresentatives.FindByNit;

/// <summary>
/// Resultado del lookup de precarga por NIT (HU #10903/#10937, ADR-0033 §5.4). Si el tenant tiene uno o
/// más representantes activos para el NIT, el FE precarga comprador/vendedor con estos datos y NO
/// consulta RUNT/RUES. Cuando la compañía tiene VARIOS representantes (multiempresa/HU #10932), la lista
/// <see cref="Representantes"/> los trae todos para que el gestor ELIJA cuál se precarga y firma; el
/// representante primario (el primero) se refleja además en <see cref="Representante"/>/
/// <see cref="FirmaVigente"/>/<see cref="IdentidadVigente"/> por compatibilidad con el consumo previo
/// (un solo representante). Las banderas de firma/identidad se calculan al momento (no son el estado
/// congelado del guardado) POR CADA representante (por su documento). <c>Company.Nit</c> y
/// <c>Documento</c> son PII (Ley 1581): solo en respuestas autenticadas; no loguear.
/// </summary>
public sealed record FindRepresentativeByNitResponse(
    RepresentativeCompanyDto Company,
    RepresentativeContactDto Representante,
    bool FirmaVigente,
    bool IdentidadVigente,
    IReadOnlyList<RepresentativeOptionDto> Representantes);

/// <summary>
/// Un representante seleccionable de la compañía (HU #10937), con sus banderas de firma/identidad
/// vigentes calculadas por su propio documento. Cuando hay más de uno, el FE renderiza un selector; el
/// elegido precarga sus datos y firma con su información. <c>Documento</c> es PII (@pii:high): no loguear.
/// </summary>
public sealed record RepresentativeOptionDto(
    string TipoDoc,
    string Documento,
    string Nombres,
    string PrimerApellido,
    string? SegundoApellido,
    string? Email,
    string? Telefono,
    bool FirmaVigente,
    bool IdentidadVigente,
    string? RazonSocial = null,
    string? CompanyEmail = null,
    string? CompanyAddress = null,
    string? CompanyCity = null,
    string? CompanyPhone = null);

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
