using Flit.Admin.Application.Companies.CreateCompany;

namespace Flit.Admin.Application.Companies.TransitOffices.GetTenantAuditLog;

/// <summary>Filtros del historial inválidos (HU #14004, AC2); la API responde 422.</summary>
public sealed class InvalidAuditLogFilterException : Exception
{
    public InvalidAuditLogFilterException(IReadOnlyList<CompanyValidationError> errors)
        : base(string.Join(" ", errors.Select(e => e.Message)))
    {
        Errors = errors;
    }

    public IReadOnlyList<CompanyValidationError> Errors { get; }
}
