namespace Flit.Admin.Application.Companies.MandateSigners.ListMandateSigners;

/// <summary>
/// Vista de un mandatario para la gestión OT. <c>DocumentNumber</c> se entrega solo en esta
/// respuesta autenticada de gestión (nunca en logs ni errores) para precargar el formulario.
/// </summary>
public sealed record MandateSignerResponse(
    Guid Id,
    Guid TransitOfficeId,
    string FullName,
    string DocumentNumber,
    string IntegrityHash,
    DateTimeOffset RegisteredAt,
    bool IsActive,
    IReadOnlyList<Guid> CompanyTenantIds);
