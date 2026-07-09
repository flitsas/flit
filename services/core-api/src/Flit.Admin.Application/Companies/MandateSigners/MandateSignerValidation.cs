using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>
/// Validaciones compartidas de alta/edición de mandatarios (ADR-0023): operabilidad del OT,
/// regla de uso RF33 (compañía activa y no bloqueada en el OT) y exclusividad estricta
/// (compañía ya tomada por otro mandatario activo). Devuelve una lista de errores 422;
/// vacía = válido. Ningún mensaje expone el número de documento (PII).
/// </summary>
internal static class MandateSignerValidation
{
    public const string OtSinAltaMessage =
        "El organismo de tránsito no está dado de alta en FLIT.";

    public const string OtInactivoMessage =
        "El organismo de tránsito está inactivo en FLIT.";

    public const string NombreRequeridoMessage = "El nombre del mandatario es obligatorio.";

    public const string DocumentoRequeridoMessage = "El número de documento es obligatorio.";

    public const string SinCompaniasMessage =
        "Debe asignar al menos una compañía al mandatario.";

    /// <summary>
    /// Valida datos básicos + OT operable. Devuelve el tenant del OT si es operable, junto a
    /// los errores encontrados (si hay errores, el tenant es <c>null</c>).
    /// </summary>
    public static (Guid? OtTenantId, List<MandateSignerValidationError> Errors) ValidateBase(
        TransitOfficeOperationalStatusItem? otStatus,
        string? fullName,
        string? documentNumber,
        IReadOnlyList<Guid> companyTenantIds)
    {
        var errors = new List<MandateSignerValidationError>();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors.Add(new MandateSignerValidationError("fullName", NombreRequeridoMessage, null));
        }

        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            // No se adjunta el valor: es PII.
            errors.Add(new MandateSignerValidationError("documentNumber", DocumentoRequeridoMessage, null));
        }

        if (companyTenantIds is null || companyTenantIds.Count == 0)
        {
            errors.Add(new MandateSignerValidationError("companyTenantIds", SinCompaniasMessage, null));
        }

        if (otStatus is null || !otStatus.HasTenant)
        {
            errors.Add(new MandateSignerValidationError("transitOfficeId", OtSinAltaMessage, null));
            return (null, errors);
        }

        if (otStatus.EstadoActivo != true)
        {
            errors.Add(new MandateSignerValidationError("transitOfficeId", OtInactivoMessage, null));
            return (null, errors);
        }

        return (otStatus.TenantId, errors);
    }

    /// <summary>
    /// RF33 (compañía activa y no bloqueada) + exclusividad. <paramref name="currentSignerId"/>
    /// excluye al propio mandatario en la edición (una compañía ya suya no es conflicto).
    /// </summary>
    public static void ValidateCompanies(
        List<MandateSignerValidationError> errors,
        IReadOnlyList<Guid> requestedCompanyIds,
        IReadOnlyList<OtCompanyOption> otCompanies,
        IReadOnlyList<MandateSignerCompanyResolution> activeResolutions,
        Guid? currentSignerId)
    {
        var companyById = otCompanies.ToDictionary(c => c.CompanyTenantId);
        var resolutionByCompany = activeResolutions.ToDictionary(r => r.CompanyTenantId);

        foreach (var companyId in requestedCompanyIds.Distinct())
        {
            // RF33: la compañía debe tener grant habilitado y estar activa en el OT.
            if (!companyById.TryGetValue(companyId, out var company) || !company.IsEnabled || !company.IsActive)
            {
                errors.Add(new MandateSignerValidationError(
                    "companyTenantIds",
                    "La compañía no está habilitada o está inactiva en el organismo de tránsito.",
                    companyId.ToString()));
                continue;
            }

            // Exclusividad: la compañía no puede estar tomada por OTRO mandatario activo.
            if (resolutionByCompany.TryGetValue(companyId, out var resolution)
                && resolution.MandateSignerId != currentSignerId)
            {
                errors.Add(new MandateSignerValidationError(
                    "companyTenantIds",
                    $"La compañía ya tiene un mandatario asignado: {resolution.FullName}.",
                    companyId.ToString()));
            }
        }
    }
}
