namespace Flit.Admin.Application.Companies.LegalRepresentatives;

/// <summary>
/// Puerto (hexagonal) para consultar, desde el módulo Admin, la validación de identidad biométrica
/// APROBADA y VIGENTE de una persona por documento — sin acoplar Admin a <c>Tramites.Domain</c>
/// (HU #10900, ADR-0033). El adaptador vive en Infrastructure y consulta
/// <c>tramites.procedure_instance_biometric_validations</c> (reuso HU #10350).
/// </summary>
public interface IRepresentativeIdentityLookup
{
    /// <summary>
    /// Id de la validación biométrica aprobada y vigente más reciente de la persona
    /// (<paramref name="tipoDocumento"/> + <paramref name="documento"/>) en el tenant, o <c>null</c>
    /// si no tiene ninguna vigente en <paramref name="now"/>.
    /// </summary>
    Task<Guid?> FindVigenteIdentityRefAsync(
        Guid tenantId,
        string tipoDocumento,
        string documento,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
