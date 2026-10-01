using Flit.Admin.Domain.Companies.MandateSigners;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.MandateSigners;

/// <summary>
/// Validaciones compartidas de alta/edición de mandatarios (ADR-0023): operabilidad del OT y RF33
/// (compañías activas/no bloqueadas). Hay a lo sumo un mandatario activo por llave cliente×OT.
/// Devuelve una lista de errores 422; vacía = válido. Ningún mensaje expone el número de documento (PII).
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

    public const string ExclusividadClienteOtMessage =
        "Ya existe un mandatario para esta empresa en este organismo.";

    /// <summary>
    /// Valida datos básicos + OT operable. Devuelve el tenant del OT si es operable, junto a
    /// los errores encontrados (si hay errores, el tenant es <c>null</c>).
    /// </summary>
    public static (Guid? OtTenantId, List<MandateSignerValidationError> Errors) ValidateBase(
        TransitOfficeOperationalStatusItem? otStatus,
        string? fullName,
        string? documentNumber,
        IReadOnlyList<Guid> companyTenantIds,
        bool documentRequired = true)
    {
        var errors = new List<MandateSignerValidationError>();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            errors.Add(new MandateSignerValidationError("fullName", NombreRequeridoMessage, null));
        }

        if (documentRequired && string.IsNullOrWhiteSpace(documentNumber))
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
    /// RF33 (compañía activa y no bloqueada) y un solo mandatario activo por llave cliente×OT.
    /// </summary>
    public static void ValidateCompanies(
        List<MandateSignerValidationError> errors,
        IReadOnlyList<Guid> requestedCompanyIds,
        IReadOnlyList<OtCompanyOption> otCompanies,
        IReadOnlyList<MandateSignerCompanyResolution> activeResolutions,
        Guid? currentSignerId,
        IReadOnlySet<Guid>? validCompanyIds = null)
    {
        var companyById = otCompanies.ToDictionary(c => c.CompanyTenantId);

        foreach (var companyId in requestedCompanyIds.Distinct())
        {
            // RF33: la compañía debe tener grant habilitado y estar activa en el OT.
            // HU #13182b: con <paramref name="validCompanyIds"/> (OT / Super Admin) basta con que sea una compañía activa.
            var invalida = validCompanyIds is not null
                ? !validCompanyIds.Contains(companyId)
                : !companyById.TryGetValue(companyId, out var company) || !company.IsEnabled || !company.IsActive;
            if (invalida)
            {
                errors.Add(new MandateSignerValidationError(
                    "companyTenantIds",
                    "La compañía no está habilitada o está inactiva en el organismo de tránsito.",
                    companyId.ToString()));
            }

            var taken = activeResolutions.FirstOrDefault(r =>
                r.CompanyTenantId == companyId
                && r.MandateSignerId != currentSignerId);
            if (taken is not null)
            {
                errors.Add(new MandateSignerValidationError(
                    "companyTenantIds",
                    ExclusividadClienteOtMessage,
                    companyId.ToString()));
            }
        }
    }

}
