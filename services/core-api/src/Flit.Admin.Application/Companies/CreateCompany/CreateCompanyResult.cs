using Flit.Admin.Domain.Companies;

namespace Flit.Admin.Application.Companies.CreateCompany;

/// <summary>
/// Resultado del alta. <see cref="IsValid"/> falso → fallo de validación (HTTP 422)
/// sin persistir nada; verdadero → compañía creada (HTTP 201) con su proyección.
/// </summary>
public sealed class CreateCompanyResult
{
    private CreateCompanyResult(
        bool isValid,
        CompanyListItem? company,
        IReadOnlyList<CompanyValidationError> errors)
    {
        IsValid = isValid;
        Company = company;
        Errors = errors;
    }

    public bool IsValid { get; }

    public CompanyListItem? Company { get; }

    public IReadOnlyList<CompanyValidationError> Errors { get; }

    public static CreateCompanyResult Success(CompanyListItem company) =>
        new(true, company, []);

    public static CreateCompanyResult Invalid(IReadOnlyList<CompanyValidationError> errors) =>
        new(false, null, errors);
}
