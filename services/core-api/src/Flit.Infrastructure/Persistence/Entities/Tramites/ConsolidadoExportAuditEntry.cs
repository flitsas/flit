using System.Net;

namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// HU #13368 (Feature #13306, ADR-0070 D8) — fila de <c>tramites.consolidado_export_audit</c>: evento Ley 1581 de un
/// lote de descarga masiva de consolidados (creación, cierre, descarga de parte, cancelación, purga). Append-only:
/// UPDATE y DELETE los rechaza <c>tr_consolidado_export_audit_immutable</c>. Sin FK ni navegaciones a propósito: la
/// fila sobrevive al lote, al usuario y a la compañía (patrón DDL 113). Solo identificadores y conteos; nunca PDF,
/// nombres, documentos ni placas.
/// </summary>
public sealed class ConsolidadoExportAuditEntry
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Uno de <c>ConsolidadoExportAuditEvent</c>.</summary>
    public string Event { get; set; } = string.Empty;

    /// <summary>Uno de <c>ConsolidadoExportOrigin</c> (desnormalizado: no hay FK al lote).</summary>
    public string Origin { get; set; } = string.Empty;

    public Guid BatchId { get; set; }

    public Guid ActorUserId { get; set; }

    /// <summary>Compañía del actor. <c>null</c> si y solo si el origen es superadmin (Q8).</summary>
    public Guid? ActorTenantId { get; set; }

    public string ActorRoleCode { get; set; } = string.Empty;

    public Guid? ScopeTenantId { get; set; }

    /// <summary>Compañías distintas de los trámites del lote. Obligatorio en <c>lote_creado</c>.</summary>
    public Guid[]? ReachedTenantIds { get; set; }

    /// <summary>Uno de <c>ConsolidadoExportDocumentType</c>.</summary>
    public string DocumentType { get; set; } = string.Empty;

    /// <summary>Uno de <c>ConsolidadoExportSelectionMode</c>; obligatorio en <c>lote_creado</c>.</summary>
    public string? SelectionMode { get; set; }

    /// <summary>Filtro aplicado como JSON, con las listas pegadas reducidas a conteos (<c>@pii:low</c>).</summary>
    public string? FilterSummary { get; set; }

    public int? IdsCount { get; set; }

    public int? ExcludedCount { get; set; }

    public int? TotalItems { get; set; }

    public int? IncludedCount { get; set; }

    public int? OmittedCount { get; set; }

    public int? GeneratedCount { get; set; }

    /// <summary>Obligatorio en <c>parte_descargada</c>.</summary>
    public short? PartNumber { get; set; }

    /// <summary>IP del actor (<c>@pii:medium</c>).</summary>
    public IPAddress? ClientIp { get; set; }

    /// <summary>Navegador del actor (<c>@pii:low</c>).</summary>
    public string? UserAgent { get; set; }

    /// <summary>Convención A5; nunca cambia (la tabla es append-only).</summary>
    public long RowVersion { get; set; }
}
