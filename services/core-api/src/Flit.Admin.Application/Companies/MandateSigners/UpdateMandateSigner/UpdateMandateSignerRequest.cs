namespace Flit.Admin.Application.Companies.MandateSigners.UpdateMandateSigner;

/// <summary>Cuerpo HTTP de edición de mandatario. La huella se regenera en el servidor.</summary>
public sealed record UpdateMandateSignerRequest(
    string? FullName,
    string? DocumentNumber,
    IReadOnlyList<Guid>? CompanyTenantIds);
