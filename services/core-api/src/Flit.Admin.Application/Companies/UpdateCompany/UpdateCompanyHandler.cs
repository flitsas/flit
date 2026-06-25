using Flit.Admin.Application.Companies.CreateCompany;
using Flit.Admin.Domain.Companies;
using Flit.Admin.Domain.Companies.Create;

namespace Flit.Admin.Application.Companies.UpdateCompany;

/// <summary>
/// Caso de uso de edición de compañía (B2B) sobre <c>identity.tenants</c>.
///
/// Flujo: (1) valida campos requeridos, longitudes y tipo permitido —cualquier fallo
/// retorna 422 con la lista de errores <em>sin tocar BD</em>; (2) delega la
/// actualización al repositorio, que devuelve <c>null</c> (→404) si el tenant no existe.
/// El <c>code</c> es inmutable, por lo que no se valida unicidad (a diferencia del alta).
/// La razón social y el NIT no exigen unicidad (pueden repetirse entre tenants).
/// </summary>
public sealed class UpdateCompanyHandler
{
    private const int LegalNameMaxLength = 255;
    private const int TaxIdMaxLength = 20;

    private readonly ICompanyWriteRepository _repository;

    public UpdateCompanyHandler(ICompanyWriteRepository repository)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    }

    public async Task<UpdateCompanyResult> HandleAsync(
        UpdateCompanyCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var request = command.Request;
        var errors = new List<CompanyValidationError>();

        var razonSocial = request.RazonSocial?.Trim() ?? string.Empty;
        var nit = request.Nit?.Trim() ?? string.Empty;
        var requestedType = request.TenantType?.Trim() ?? string.Empty;

        if (razonSocial.Length == 0)
        {
            errors.Add(new CompanyValidationError("razonSocial", "La razón social es obligatoria."));
        }
        else if (razonSocial.Length > LegalNameMaxLength)
        {
            errors.Add(new CompanyValidationError(
                "razonSocial", $"La razón social no puede superar {LegalNameMaxLength} caracteres."));
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

        // El listado administrativo incluye tenants con tipos heredados fuera del catálogo
        // B2B (p.ej. 'standard', 'transit_office'). Para no bloquear la edición de esas
        // compañías ni corromper su tipo, se resuelve respecto del valor actual: si el tipo
        // pedido coincide con el existente (ignorando mayúsculas) se preserva tal cual; en
        // caso contrario debe ser uno del catálogo B2B (normalizado a mayúsculas).
        var current = await _repository
            .GetByIdAsync(command.TenantId, cancellationToken)
            .ConfigureAwait(false);

        if (current is null)
        {
            return UpdateCompanyResult.NotFound();
        }

        // Concurrencia optimista: si el cliente envió la versión que leyó y ya no coincide
        // con la actual, otra persona editó la compañía entre tanto → 409 (lost update).
        if (request.RowVersion is { } expectedVersion && expectedVersion != current.RowVersion)
        {
            return UpdateCompanyResult.Conflict();
        }

        string effectiveType;
        if (string.Equals(requestedType, current.TenantType, StringComparison.OrdinalIgnoreCase))
        {
            // Sin cambio de tipo: se conserva el valor existente exactamente (incl. heredados).
            effectiveType = current.TenantType;
        }
        else
        {
            var normalized = requestedType.ToUpperInvariant();
            if (CompanyTenantTypes.IsValid(normalized))
            {
                effectiveType = normalized;
            }
            else
            {
                effectiveType = string.Empty;
                errors.Add(new CompanyValidationError(
                    "tenantType", "El tipo de compañía debe ser RENTING, CONCESIONARIO o FLIT."));
            }
        }

        if (errors.Count > 0)
        {
            return UpdateCompanyResult.Invalid(errors);
        }

        var company = await _repository
            .UpdateAsync(
                command.TenantId,
                razonSocial,
                nit,
                effectiveType,
                request.EstadoActivo ?? true,
                command.ChangedBy,
                cancellationToken)
            .ConfigureAwait(false);

        return company is null
            ? UpdateCompanyResult.NotFound()
            : UpdateCompanyResult.Success(company);
    }
}
