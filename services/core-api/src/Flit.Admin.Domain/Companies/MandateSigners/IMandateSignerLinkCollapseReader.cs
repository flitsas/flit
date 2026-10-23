namespace Flit.Admin.Domain.Companies.MandateSigners;

/// <summary>
/// HU #13195 (ADR-0066 D1) — una fila del reporte previo del colapso de vínculos mandatario-compañía: un
/// vínculo activo de un grupo (organismo, compañía, grupo de origen) con más de uno, y si se conserva o se
/// inactivará. <b>Sin datos personales</b>: solo ids (Ley 1581); el mandatario se identifica por su id.
/// </summary>
/// <param name="TransitOfficeId">Organismo del grupo.</param>
/// <param name="TransitOfficeCode">Código del organismo.</param>
/// <param name="CompanyTenantId">Compañía del grupo.</param>
/// <param name="OriginGroup">Grupo de origen: <c>organismo</c> (incluye super_admin) o <c>compania</c>.</param>
/// <param name="GroupSize">Vínculos activos del grupo.</param>
/// <param name="LinkId">Vínculo evaluado.</param>
/// <param name="MandateSignerId">Mandatario del vínculo.</param>
/// <param name="Action">Uno de <see cref="MandateLinkCollapseActions"/>.</param>
/// <param name="Criterion">Por qué se conserva el del grupo: uno de <see cref="MandateLinkCollapseCriteria"/>.</param>
public sealed record MandateLinkCollapseRow(
    Guid TransitOfficeId,
    string TransitOfficeCode,
    Guid CompanyTenantId,
    string OriginGroup,
    int GroupSize,
    Guid LinkId,
    Guid MandateSignerId,
    string Action,
    string Criterion);

/// <summary>Valores de <see cref="MandateLinkCollapseRow.Action"/>.</summary>
public static class MandateLinkCollapseActions
{
    public const string Conservar = "conservar";
    public const string Inactivar = "inactivar";
}

/// <summary>Valores de <see cref="MandateLinkCollapseRow.Criterion"/>.</summary>
public static class MandateLinkCollapseCriteria
{
    public const string DesignadoEnRegla = "designado_en_regla";
    public const string FirmaValida = "firma_valida";
    public const string MasReciente = "mas_reciente";
}

/// <summary>
/// HU #13195 — puerto del reporte previo del colapso. SOLO LECTURA y sin alcance de tenant: lo consume
/// únicamente el Super Admin (lo impone el endpoint, que responde 403 al resto).
/// </summary>
public interface IMandateSignerLinkCollapseReader
{
    /// <summary>Grupos con más de un vínculo activo y qué pasaría con cada vínculo. No modifica datos.</summary>
    Task<IReadOnlyList<MandateLinkCollapseRow>> ListAsync(
        Guid? transitOfficeId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// HU #13195 (AC3) — el índice único <c>uq_mandate_signer_companies_one_per_origin</c> rechazó activar un
/// segundo vínculo mandatario-compañía para el mismo organismo y grupo de origen. La API lo traduce a 409.
/// El mensaje no lleva datos personales.
/// </summary>
public sealed class MandateSignerActiveLinkConflictException : Exception
{
    public const string DefaultMessage =
        "Ya existe un mandatario activo para esta compañía en este organismo. Inactívalo o edítalo antes de asignar otro.";

    public MandateSignerActiveLinkConflictException()
        : base(DefaultMessage)
    {
    }

    public MandateSignerActiveLinkConflictException(string message)
        : base(message)
    {
    }

    public MandateSignerActiveLinkConflictException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
