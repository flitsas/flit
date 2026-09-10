using Flit.Admin.Application.Common;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Application.Companies.Children.CreateChildCompany;

/// <summary>HU #12345 AC1 — alta de cliente hijo bajo una cabeza de grupo.</summary>
public sealed class CreateChildCompanyHandler
{
    private const int LegalNameMaxLength = 255;
    private const int TaxIdMaxLength = 20;
    private const int CodeMaxLength = 32;

    private readonly ICompanyHierarchyRepository _hierarchy;
    private readonly ICompanyWriteRepository _companies;

    public CreateChildCompanyHandler(
        ICompanyHierarchyRepository hierarchy,
        ICompanyWriteRepository companies)
    {
        _hierarchy = hierarchy ?? throw new ArgumentNullException(nameof(hierarchy));
        _companies = companies ?? throw new ArgumentNullException(nameof(companies));
    }

    public async Task<CreateCompanyResult> HandleAsync(
        CreateChildCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<CompanyValidationError>();

        var razonSocial = request.RazonSocial?.Trim() ?? string.Empty;
        var nit = request.Nit?.Trim() ?? string.Empty;
        var code = request.Code?.Trim() ?? string.Empty;
        var tenantTypeRaw = request.TenantType?.Trim();
        var tenantType = string.IsNullOrEmpty(tenantTypeRaw)
            ? ChildTenantTypes.DefaultForHeadType(command.HeadTenantType)
            : tenantTypeRaw.ToUpperInvariant();

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
            errors.Add(new CompanyValidationError("nit", $"El NIT no puede superar {TaxIdMaxLength} caracteres."));
        }
        else if (!TextFieldPatterns.TaxId().IsMatch(nit))
        {
            errors.Add(new CompanyValidationError("nit", "El NIT solo permite dígitos, puntos y guiones."));
        }
        else if (!TextFieldPatterns.HasDigit().IsMatch(nit))
        {
            errors.Add(new CompanyValidationError("nit", "El NIT debe contener al menos un dígito."));
        }

        if (code.Length == 0)
        {
            errors.Add(new CompanyValidationError("code", "El código es obligatorio."));
        }
        else if (code.Length > CodeMaxLength)
        {
            errors.Add(new CompanyValidationError("code", $"El código no puede superar {CodeMaxLength} caracteres."));
        }
        else if (!TextFieldPatterns.TenantCode().IsMatch(code))
        {
            errors.Add(new CompanyValidationError(
                "code", "El código solo permite letras, números, guion y guion bajo."));
        }
        else if (!TextFieldPatterns.HasLetterOrDigit().IsMatch(code))
        {
            errors.Add(new CompanyValidationError("code", "El código debe contener al menos una letra o número."));
        }

        if (!ChildTenantTypes.IsValid(tenantType))
        {
            errors.Add(new CompanyValidationError(
                "tenantType",
                $"El tipo del cliente hijo debe ser {ChildTenantTypes.DisplayList}. "
                + "Un tipo de cabeza de grupo o FLIT no está permitido."));
        }

        if (code.Length is > 0 and <= CodeMaxLength
            && await _companies.CodeExistsAsync(code, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new CompanyValidationError("code", "Ya existe una compañía con ese código."));
        }

        if (nit.Length is > 0 and <= TaxIdMaxLength
            && await _companies.TaxIdExistsAsync(nit, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new CompanyValidationError("nit", "Ya existe una compañía con ese NIT."));
        }

        if (errors.Count > 0)
        {
            return CreateCompanyResult.Invalid(errors);
        }

        try
        {
            var company = await _hierarchy
                .CreateChildAsync(
                    new NewChildCompany(
                        LegalName: razonSocial,
                        TaxId: nit,
                        Code: code,
                        TenantType: tenantType,
                        IsActive: request.EstadoActivo ?? true,
                        ParentTenantId: command.HeadTenantId,
                        CreatedBy: command.CreatedBy),
                    cancellationToken)
                .ConfigureAwait(false);

            return CreateCompanyResult.Success(company);
        }
        catch (CompanyLinkRejectedException ex)
        {
            errors.Add(new CompanyValidationError("tenantType", ex.Message));
            return CreateCompanyResult.Invalid(errors);
        }
    }
}
