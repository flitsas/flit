namespace Flit.Admin.Application.Companies.MandateSigners.ListOtCompanies;

/// <summary>
/// Compañía del OT con su mandatario resuelto (RF34). <c>AssignedSigner*</c> nulo = la compañía
/// no tiene mandatario (RF26): al generar su mandato solo se advierte, no se bloquea.
/// </summary>
public sealed record OtCompanyResponse(
    Guid CompanyTenantId,
    string LegalName,
    bool IsActive,
    bool IsEnabled,
    Guid? AssignedSignerId,
    string? AssignedSignerName,
    string? AssignedSignerHash);
