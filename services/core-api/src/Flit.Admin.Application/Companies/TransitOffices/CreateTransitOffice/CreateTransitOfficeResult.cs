using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies.TransitOffices;

namespace Flit.Admin.Application.Companies.TransitOffices.CreateTransitOffice;

/// <summary>
/// Resultado del alta de tenant OT. <see cref="IsValid"/> falso → fallo de validación
/// (HTTP 422) sin persistir nada; verdadero → tenant OT creado (HTTP 201) con su
/// proyección. Reutiliza <see cref="CompanyValidationError"/> (mismo shape <c>{ field,
/// message }</c>) para no duplicar el tipo de error 422 entre compañías y OT.
/// </summary>
public sealed class CreateTransitOfficeResult
{
    private CreateTransitOfficeResult(
        bool isValid,
        TransitOfficeTenantItem? transitOffice,
        IReadOnlyList<CompanyValidationError> errors)
    {
        IsValid = isValid;
        TransitOffice = transitOffice;
        Errors = errors;
    }

    public bool IsValid { get; }

    public TransitOfficeTenantItem? TransitOffice { get; }

    public IReadOnlyList<CompanyValidationError> Errors { get; }

    public static CreateTransitOfficeResult Success(TransitOfficeTenantItem transitOffice) =>
        new(true, transitOffice, []);

    public static CreateTransitOfficeResult Invalid(IReadOnlyList<CompanyValidationError> errors) =>
        new(false, null, errors);
}
