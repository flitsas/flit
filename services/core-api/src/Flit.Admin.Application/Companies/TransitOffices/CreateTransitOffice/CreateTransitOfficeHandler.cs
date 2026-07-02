using Flit.Admin.Application.Common;
using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies.TransitOffices;
using Flit.Admin.Domain.Companies.TransitOffices.Create;
using Flit.Admin.Domain.OtProfile;

namespace Flit.Admin.Application.Companies.TransitOffices.CreateTransitOffice;

/// <summary>
/// Caso de uso de alta de un tenant Organismo de Tránsito (OT) sobre
/// <c>identity.tenants</c> + <c>admin.transit_office_profiles</c>.
///
/// Flujo: (1) valida campos requeridos, longitudes y formato —mismas reglas que
/// <see cref="Flit.Admin.Application.Companies.CreateCompany.CreateCompanyHandler"/>
/// para razón social/NIT/código—, más las reglas propias de OT: la oficina del
/// catálogo debe existir (y estar activa) y no tener ya un <c>TransitOfficeProfile</c>
/// asociado (una oficina física = un solo tenant OT); cualquier fallo retorna 422 con
/// la lista de errores <em>sin tocar BD</em>; (2) verifica unicidad del <c>code</c>;
/// (3) delega el alta compuesta (tenant + rol <c>ot_admin</c> sin permisos + perfil OT)
/// al repositorio.
///
/// Decisión de producto (no reabrir, ver plan de refactor "adminOT"): el
/// <c>TenantType</c> del tenant OT se fija explícitamente en <c>RENTING</c> — la
/// migración <c>20260630180000_RestrictTenantTypeCatalog</c> restringió
/// <c>identity.tenants.tenant_type</c> al catálogo B2B (<c>RENTING</c> |
/// <c>CONCESIONARIO</c> | <c>FLIT</c>) y no se reintroduce un tipo dedicado para OT;
/// <c>TransitOfficeProfile</c> sigue siendo la única fuente de verdad de que un
/// tenant "es" un OT.
/// </summary>
public sealed class CreateTransitOfficeHandler
{
    private const int LegalNameMaxLength = 255;
    private const int TaxIdMaxLength = 20;
    private const int CodeMaxLength = 32;

    private readonly ITransitOfficeTenantWriteRepository _repository;
    private readonly ITransitOfficeCatalog _catalog;

    public CreateTransitOfficeHandler(
        ITransitOfficeTenantWriteRepository repository,
        ITransitOfficeCatalog catalog)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public async Task<CreateTransitOfficeResult> HandleAsync(
        CreateTransitOfficeCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<CompanyValidationError>();

        var legalName = request.LegalName?.Trim() ?? string.Empty;
        var taxId = request.TaxId?.Trim() ?? string.Empty;
        var code = request.Code?.Trim() ?? string.Empty;
        var operationMode = string.IsNullOrWhiteSpace(request.OperationMode)
            ? OtOperationModes.Dashboard
            : request.OperationMode.Trim();

        if (legalName.Length == 0)
        {
            errors.Add(new CompanyValidationError("legalName", "La razón social es obligatoria."));
        }
        else if (legalName.Length > LegalNameMaxLength)
        {
            errors.Add(new CompanyValidationError(
                "legalName", $"La razón social no puede superar {LegalNameMaxLength} caracteres."));
        }
        else if (TextFieldPatterns.ValidateReadableName(legalName, "La razón social") is { } razonError)
        {
            errors.Add(new CompanyValidationError("legalName", razonError));
        }

        if (taxId.Length == 0)
        {
            errors.Add(new CompanyValidationError("taxId", "El NIT es obligatorio."));
        }
        else if (taxId.Length > TaxIdMaxLength)
        {
            errors.Add(new CompanyValidationError(
                "taxId", $"El NIT no puede superar {TaxIdMaxLength} caracteres."));
        }
        else if (!TextFieldPatterns.TaxId().IsMatch(taxId))
        {
            errors.Add(new CompanyValidationError(
                "taxId", "El NIT solo permite dígitos, puntos y guiones."));
        }
        else if (!TextFieldPatterns.HasDigit().IsMatch(taxId))
        {
            errors.Add(new CompanyValidationError(
                "taxId", "El NIT debe contener al menos un dígito."));
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

        if (!OtOperationModes.IsValid(operationMode))
        {
            errors.Add(new CompanyValidationError(
                "operationMode",
                $"Valor inválido. Valores permitidos: {OtOperationModes.Dashboard}, {OtOperationModes.Quipux}."));
        }

        if (request.TransitOfficeId is null || request.TransitOfficeId == Guid.Empty)
        {
            errors.Add(new CompanyValidationError(
                "transitOfficeId", "Debe seleccionar una oficina del catálogo de organismos de tránsito."));
        }
        else if (!_catalog.Exists(request.TransitOfficeId.Value))
        {
            errors.Add(new CompanyValidationError(
                "transitOfficeId", "La oficina seleccionada no existe en el catálogo o está inactiva."));
        }
        else if (await _repository
                     .TransitOfficeHasProfileAsync(request.TransitOfficeId.Value, cancellationToken)
                     .ConfigureAwait(false))
        {
            errors.Add(new CompanyValidationError(
                "transitOfficeId", "La oficina seleccionada ya tiene un tenant OT asociado."));
        }

        // Unicidad del code: solo se consulta si el code pasó las validaciones de formato.
        if (code.Length is > 0 and <= CodeMaxLength
            && await _repository.CodeExistsAsync(code, cancellationToken).ConfigureAwait(false))
        {
            errors.Add(new CompanyValidationError("code", "Ya existe un tenant con ese código."));
        }

        if (errors.Count > 0)
        {
            return CreateTransitOfficeResult.Invalid(errors);
        }

        var transitOffice = await _repository
            .CreateAsync(
                new NewTransitOffice(
                    TransitOfficeId: request.TransitOfficeId!.Value,
                    LegalName: legalName,
                    TaxId: taxId,
                    Code: code,
                    OperationMode: operationMode,
                    CreatedBy: command.CreatedBy),
                cancellationToken)
            .ConfigureAwait(false);

        return CreateTransitOfficeResult.Success(transitOffice);
    }
}
