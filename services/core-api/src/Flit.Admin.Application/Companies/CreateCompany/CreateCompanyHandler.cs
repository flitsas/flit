using Flit.Admin.Application.Common;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Application.Companies.CreateCompany;

/// <summary>
/// Caso de uso de alta de compañía (B2B) sobre <c>identity.tenants</c>.
///
/// Flujo: (1) valida campos requeridos, longitudes y tipo permitido —cualquier fallo
/// retorna 422 con la lista de errores <em>sin tocar BD</em>; (2) verifica unicidad
/// del <c>code</c> (único en BD); (3) delega la inserción al repositorio. La razón
/// social y el NIT no exigen unicidad (pueden repetirse entre tenants); el code sí.
/// </summary>
public sealed class CreateCompanyHandler
{
    private const int LegalNameMaxLength = 255;
    private const int TaxIdMaxLength = 20;
    private const int CodeMaxLength = 32;

    private readonly ICompanyWriteRepository _repository;

    public CreateCompanyHandler(ICompanyWriteRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<CreateCompanyResult> HandleAsync(
        CreateCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<CompanyValidationError>();

        var razonSocial = request.RazonSocial?.Trim() ?? string.Empty;
        var nit = request.Nit?.Trim() ?? string.Empty;
        var code = request.Code?.Trim() ?? string.Empty;
        var tenantType = request.TenantType?.Trim().ToUpperInvariant() ?? string.Empty;

        if (razonSocial.Length == 0)
        {
            errors.Add(new CompanyValidationError("razonSocial", "La razón social es obligatoria."));
        }
        else if (razonSocial.Length > LegalNameMaxLength)
        {
            errors.Add(new CompanyValidationError(
                "razonSocial", $"La razón social no puede superar {LegalNameMaxLength} caracteres."));
        }
        else if (TextFieldPatterns.ValidateReadableName(razonSocial, "La razón social") is { } razonError)
        {
            errors.Add(new CompanyValidationError("razonSocial", razonError));
        }

        if (nit.Length == 0)
        {
            errors.Add(new CompanyValidationError("nit", "El NIT es obligatorio."));
        }
        else if (nit.Length > TaxIdMaxLength)
        {
            errors.Add(new CompanyValidationError(
                "nit", $"El NIT no puede superar {TaxIdMaxLength} caracteres."));
        }
        else if (!TextFieldPatterns.TaxId().IsMatch(nit))
        {
            errors.Add(new CompanyValidationError(
                "nit", "El NIT solo permite dígitos, puntos y guiones."));
        }
        else if (!TextFieldPatterns.HasDigit().IsMatch(nit))
        {
            errors.Add(new CompanyValidationError(
                "nit", "El NIT debe contener al menos un dígito."));
        }

        if (code.Length == 0)
        {
            errors.Add(new CompanyValidationError("code", "El código es obligatorio."));
        }
        else if (code.Length > CodeMaxLength)
        {
            errors.Add(new CompanyValidationError(
                "code", $"El código no puede superar {CodeMaxLength} caracteres."));
        }
        else if (!TextFieldPatterns.TenantCode().IsMatch(code))
        {
            errors.Add(new CompanyValidationError(
                "code", "El código solo permite letras, números, guion y guion bajo."));
        }
        else if (!TextFieldPatterns.HasLetterOrDigit().IsMatch(code))
        {
            errors.Add(new CompanyValidationError(
                "code", "El código debe contener al menos una letra o número."));
        }

        if (!CompanyTenantTypes.IsValid(tenantType))
        {
            errors.Add(new CompanyValidationError(
                "tenantType", "El tipo de compañía debe ser RENTING, CONCESIONARIO o FLIT."));
        }

        // Unicidad del code: solo se consulta si el code pasó las validaciones de formato.
        if (code.Length is > 0 and <= CodeMaxLength
            && await _repository.CodeExistsAsync(code, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new CompanyValidationError("code", "Ya existe una compañía con ese código."));
        }

        if (errors.Count > 0)
        {
            return CreateCompanyResult.Invalid(errors);
        }

        var company = await _repository
            .CreateAsync(
                new NewCompany(
                    LegalName: razonSocial,
                    TaxId: nit,
                    Code: code,
                    TenantType: tenantType,
                    IsActive: request.EstadoActivo ?? true,
                    CreatedBy: command.CreatedBy),
                cancellationToken)
            .ConfigureAwait(false);

        return CreateCompanyResult.Success(company);
    }
}
