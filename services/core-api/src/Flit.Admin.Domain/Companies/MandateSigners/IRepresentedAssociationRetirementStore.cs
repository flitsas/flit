namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13176 (Feature #13119 F7) — una fila del reporte de mandatarios que hoy dependen de una asociación por
/// Representante Legal (<c>mandate_signer_represented_companies</c>): por compañía y organismo, el mandatario,
/// cuántas empresas tiene asociadas y sus NIT. <b>Sin documento de identidad ni ruta de firma</b> del mandatario
/// (Ley 1581): solo el nombre que exige el reporte.
/// </summary>
public sealed record RepresentedAssociationImpactRow(
    Guid CompanyTenantId,
    string CompanyName,
    Guid TransitOfficeId,
    string TransitOfficeName,
    Guid MandateSignerId,
    string MandateSignerName,
    int AssociatedCount,
    IReadOnlyList<string> AssociatedNits);

/// <summary>
/// HU #13176 — puerto del reporte y del retiro de las asociaciones por Representante Legal. La tabla legada
/// queda sin uso (su borrado físico es de la limpieza F8). No toca <c>admin.represented_companies</c> ni
/// <c>ListRepresentedCompaniesAsync</c>: los usa el flujo de escrituras.
/// </summary>
public interface IRepresentedAssociationRetirementStore
{
    /// <summary>Mandatarios activos con asociaciones activas por Representante Legal. Solo lectura.</summary>
    Task<IReadOnlyList<RepresentedAssociationImpactRow>> ListImpactedAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Elimina las filas ACTIVAS de asociación por Representante Legal y devuelve cuántas retiró (0 si ya no
    /// hay). Los mandatarios afectados pasan a aplicar a toda su compañía en el organismo.
    /// </summary>
    Task<int> RetireActiveAsync(CancellationToken cancellationToken = default);
}

/// <summary>HU #13176 (AC3) — el retiro se invocó sin confirmar el aviso previo a los clientes: la API responde 409.</summary>
public sealed class RepresentedAssociationNoticeNotConfirmedException : Exception
{
    public const string Code = "aviso_no_confirmado";

    public const string DefaultMessage =
        "Confirma que el aviso a los clientes fue enviado antes de retirar las asociaciones por Representante Legal.";

    public RepresentedAssociationNoticeNotConfirmedException()
        : base(DefaultMessage)
    {
    }

    public RepresentedAssociationNoticeNotConfirmedException(string message)
        : base(message)
    {
    }

    public RepresentedAssociationNoticeNotConfirmedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
