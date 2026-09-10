namespace Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;

/// <summary>
/// Un mandatario activo asignado a una compañía dentro del OT (ADR-0036). Sustituye al antiguo
/// <c>AssignedSigner*</c> único: con la MULTIPLICIDAD una compañía puede tener VARIOS mandatarios.
/// </summary>
public sealed record AssignedSignerDto(Guid MandateSignerId, string FullName, string IntegrityHash);

/// <summary>
/// Compañía del OT con sus mandatarios resueltos (RF34, ADR-0036). <c>AssignedSigners</c> vacío = la
/// compañía no tiene mandatario (RF26): al generar su mandato solo se advierte, no se bloquea. Con la
/// multiplicidad (ADR-0036, supersede la exclusividad de ADR-0023) la lista puede traer varios.
/// </summary>
public sealed record OtCompanyResponse(
    Guid CompanyTenantId,
    string LegalName,
    bool IsActive,
    bool IsEnabled,
    IReadOnlyList<AssignedSignerDto> AssignedSigners,
    /// <summary>
    /// NIT de la compañía. Sin él, dos empresas homónimas eran indistinguibles en pantalla.
    /// </summary>
    string? TaxId = null);
