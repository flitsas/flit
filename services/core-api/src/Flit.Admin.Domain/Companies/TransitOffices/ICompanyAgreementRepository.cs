namespace Flit.Admin.Domain.Companies.TransitOffices;

/// <summary>
/// Convenio comercial entre una compañía gestora y un organismo de tránsito.
///
/// <para><b>No confundir con <see cref="ITransitGrantRepository"/>.</b> Aquel gestiona el PERMISO para
/// radicar, que la radicación exige; este, un acuerdo comercial cuyo único efecto hoy es documental:
/// con convenio, el contrato de mandato no lleva bloque de firma del mandatario.</para>
/// </summary>
public interface ICompanyAgreementRepository
{
    /// <summary>
    /// Marca o desmarca el convenio de la compañía con el organismo. Idempotente: repetir la misma
    /// marca no crea filas ni falla. La fila se conserva y se conmuta <c>is_active</c>, de modo que
    /// retirar y volver a marcar reutiliza el registro en vez de acumular histórico duplicado.
    /// </summary>
    Task<bool> SetAsync(
        Guid companyTenantId,
        Guid transitOfficeId,
        bool isActive,
        Guid? changedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Organismos con los que la compañía tiene convenio ACTIVO.</summary>
    Task<IReadOnlyList<Guid>> ListActiveOfficeIdsAsync(
        Guid companyTenantId,
        CancellationToken cancellationToken = default);
}
