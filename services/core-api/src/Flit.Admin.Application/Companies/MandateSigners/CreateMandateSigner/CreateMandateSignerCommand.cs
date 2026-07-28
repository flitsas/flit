namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>Alta de un mandatario en un OT. <c>DocumentNumber</c> es PII: no loguear.</summary>
public sealed class CreateMandateSignerCommand
{
    public required Guid TransitOfficeId { get; init; }
    public required string FullName { get; init; }
    public required string DocumentNumber { get; init; }
    public required IReadOnlyList<Guid> CompanyTenantIds { get; init; }

    /// <summary>Tipo de documento (ADR-0036); por defecto CC.</summary>
    public string DocumentType { get; init; } = "CC";

    /// <summary>Correo para la validación de identidad (ADR-0036, HU #10911). PII.</summary>
    public string? Email { get; init; }

    /// <summary>Cuenta de usuario de OT del mandatario (ADR-0036 §D9).</summary>
    public Guid? UserId { get; init; }

    public Guid? CreatedBy { get; init; }
    public Guid? CorrelationId { get; init; }
}
