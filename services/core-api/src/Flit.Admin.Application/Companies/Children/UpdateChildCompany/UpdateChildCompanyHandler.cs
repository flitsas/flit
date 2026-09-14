using Flit.Admin.Application.Common;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Application.Companies.UpdateCompany;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Application.Companies.Children.UpdateChildCompany;

/// <summary>HU #12345 AC2 — edición acotada a hijos propios.</summary>
public sealed class UpdateChildCompanyHandler
{
    private const int LegalNameMaxLength = 255;
    private const int TaxIdMaxLength = 20;

    private readonly ICompanyWriteRepository _repository;

    public UpdateChildCompanyHandler(ICompanyWriteRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateCompanyResult> HandleAsync(
        UpdateChildCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<CompanyValidationError>();

        var existing = await _repository
            .GetByIdAsync(command.ChildTenantId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return UpdateCompanyResult.NotFound();
        }

        var razonSocial = request.RazonSocial?.Trim() ?? string.Empty;
        var nit = request.Nit?.Trim() ?? string.Empty;
        var requestedType = request.TenantType?.Trim().ToUpperInvariant() ?? string.Empty;

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

        var tenantType = string.Equals(requestedType, existing.TenantType, StringComparison.OrdinalIgnoreCase)
            ? existing.TenantType
            : requestedType;

        if (!ChildTenantTypes.IsValid(tenantType))
        {
            errors.Add(new CompanyValidationError(
                "tenantType",
                $"El tipo del cliente hijo debe ser {ChildTenantTypes.DisplayList}."));
        }

        if (errors.Count > 0)
        {
            return UpdateCompanyResult.Invalid(errors);
        }

        try
        {
            var company = await _repository
                .UpdateAsync(
                    command.ChildTenantId,
                    razonSocial,
                    nit,
                    tenantType,
                    request.EstadoActivo ?? existing.EstadoActivo,
                    command.ChangedBy,
                    cancellationToken)
                .ConfigureAwait(false);

            return company is null
                ? UpdateCompanyResult.NotFound()
                : UpdateCompanyResult.Success(company);
        }
        catch (CompanyHierarchyRejectedException ex)
        {
            errors.Add(new CompanyValidationError("tenantType", ex.Message));
            return UpdateCompanyResult.Invalid(errors);
        }
    }
}
