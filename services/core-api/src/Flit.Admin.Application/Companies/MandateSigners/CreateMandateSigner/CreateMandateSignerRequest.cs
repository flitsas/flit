namespace Flit.Admin.Application.Companies.MandateSigners.CreateMandateSigner;

/// <summary>Cuerpo HTTP de alta de mandatario. La huella se autogenera en el servidor.</summary>
public sealed record CreateMandateSignerRequest(
    string? FullName,
    string? DocumentNumber,
    IReadOnlyList<Guid>? CompanyTenantIds);
