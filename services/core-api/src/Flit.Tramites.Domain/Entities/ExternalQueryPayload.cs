namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Respuesta cruda <b>sanitizada</b> de una consulta externa, guardada para poder reprocesar un mapeo
/// corregido sin volver a pagar la consulta (HU #11302, ADR-0041).
/// </summary>
/// <remarks>
/// <b>PII</b>: <see cref="Payload"/> va marcado <c>@pii:high</c> en el DDL. No se vuelca en trazas,
/// logs, PRs ni comentarios de ADO (Ley 1581). Retención indefinida por decisión del PO (D6):
/// <see cref="ExpiresAt"/> queda en <c>null</c> y no hay job de purga.
/// </remarks>
public sealed class ExternalQueryPayload
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid ProcedureInstanceId { get; set; }

    /// <summary>kyverum_runt | verifik | intempo | ocr | manual | legacy</summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>vehicle | company | person</summary>
    public string SubjectKind { get; set; } = string.Empty;

    /// <summary>Placa/VIN o NIT. El sujeto, no el dato: localiza el payload sin abrirlo.</summary>
    public string? SubjectKey { get; set; }

    /// <summary>JSON sanitizado tal como respondió el proveedor.</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset QueriedAt { get; set; }

    /// <summary>D6: nulo (retención indefinida). Se conserva para acotar el plazo sin migración.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public long RowVersion { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Guid? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? UpdatedBy { get; set; }
}
