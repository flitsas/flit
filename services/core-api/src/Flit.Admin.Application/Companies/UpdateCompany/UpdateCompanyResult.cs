using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.UpdateCompany;

/// <summary>Desenlace de la edición de una compañía.</summary>
public enum UpdateCompanyOutcome
{
    /// <summary>Cambios aplicados (HTTP 200) — incluye la proyección actualizada.</summary>
    Updated,

    /// <summary>Fallo de validación (HTTP 422) — no se persistió nada.</summary>
    Invalid,

    /// <summary>La compañía no existe (HTTP 404).</summary>
    NotFound,

    /// <summary>
    /// Edición concurrente detectada (HTTP 409): la versión enviada por el cliente no
    /// coincide con la actual del tenant (otra persona la modificó). No se persiste nada.
    /// </summary>
    Conflict,
}

/// <summary>
/// Resultado de la edición. <see cref="Outcome"/> determina el HTTP: Updated→200 con
/// proyección, Invalid→422 con <see cref="Errors"/>, NotFound→404. Reutiliza
/// <see cref="CompanyValidationError"/> del alta para un contrato de error uniforme.
/// </summary>
public sealed class UpdateCompanyResult
{
    private UpdateCompanyResult(
        UpdateCompanyOutcome outcome,
        CompanyListItem? company,
        IReadOnlyList<CompanyValidationError> errors)
    {
        Outcome = outcome;
        Company = company;
        Errors = errors;
    }

    public UpdateCompanyOutcome Outcome { get; }

    public CompanyListItem? Company { get; }

    public IReadOnlyList<CompanyValidationError> Errors { get; }

    public static UpdateCompanyResult Success(CompanyListItem company) =>
        new(UpdateCompanyOutcome.Updated, company, []);

    public static UpdateCompanyResult Invalid(IReadOnlyList<CompanyValidationError> errors) =>
        new(UpdateCompanyOutcome.Invalid, null, errors);

    public static UpdateCompanyResult NotFound() =>
        new(UpdateCompanyOutcome.NotFound, null, []);

    public static UpdateCompanyResult Conflict() =>
        new(UpdateCompanyOutcome.Conflict, null, []);
}
