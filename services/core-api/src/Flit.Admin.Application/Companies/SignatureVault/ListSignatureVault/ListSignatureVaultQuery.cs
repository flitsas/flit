namespace Flit.Admin.Application.Companies.SignatureVault.ListSignatureVault;

/// <summary>
/// Consulta de las firmas del baúl de un tenant (activas y revocadas). Soporta filtrado opcional
/// por (documentType + documentNumber) para acotar a las firmas de una persona específica (HU #11175,
/// AC1) y por <see cref="SoloVigentes"/> para excluir las vencidas (AC2). <c>DocumentNumber</c> es
/// PII (Ley 1581): nunca loguear ni incluir en errores.
/// </summary>
public sealed class ListSignatureVaultQuery
{
    public required Guid TenantId { get; init; }

    /// <summary>
    /// Tipo de documento del representante. Requerido si se filtra por documento; se ignora si
    /// <see cref="DocumentNumber"/> es nulo o vacío.
    /// </summary>
    public string? DocumentType { get; init; }

    /// <summary>
    /// Número de documento del representante (PII). Si viene, la respuesta incluye solo las firmas
    /// de esa persona.
    /// </summary>
    public string? DocumentNumber { get; init; }

    /// <summary>
    /// Si <c>true</c>, excluye las firmas cuya vigencia ya expiró a la hora de Colombia (UTC-5,
    /// sin DST).
    /// </summary>
    public bool? SoloVigentes { get; init; }
}
