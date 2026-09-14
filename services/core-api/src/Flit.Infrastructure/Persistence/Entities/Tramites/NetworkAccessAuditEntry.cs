namespace Flit.Infrastructure.Persistence.Entities.Tramites;

/// <summary>
/// Fila de <c>tramites.network_access_audit</c> (HU #12361, Feature #12257): un acceso consolidado de
/// una cabeza de red a datos de sus clientes hijos. UN registro por petición (AC8); solo se escribe
/// cuando el resultado alcanzó al menos un hijo (AC5). Append-only: UPDATE y DELETE los rechaza la base
/// (<c>tr_network_access_audit_immutable</c>). Sin FK a <c>identity.tenants</c> ni a
/// <c>tramites.procedure_instances</c> a propósito: la fila sobrevive al desvínculo (AC3).
/// Solo identificadores — nunca datos personales del trámite.
/// </summary>
public sealed class NetworkAccessAuditEntry
{
    public Guid Id { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    /// <summary>Usuario de la cabeza que ejecutó la consulta o descarga; <c>null</c> si no es resoluble.</summary>
    public Guid? ActorUserId { get; set; }

    /// <summary>Cliente cabeza de red al que pertenece el actor.</summary>
    public Guid ActorTenantId { get; set; }

    /// <summary>Hijos DISTINTOS alcanzados por el resultado (nunca vacío; nunca la propia cabeza).</summary>
    public Guid[] ReachedTenantIds { get; set; } = [];

    /// <summary>Recurso consultado (<c>network.instances.search</c>, <c>network.instances.detail</c>, …).</summary>
    public string Resource { get; set; } = string.Empty;

    /// <summary>Filtros aplicados serializados como JSON (solo identificadores y valores de filtro, sin PII).</summary>
    public string? Filters { get; set; }

    /// <summary>Trámite consultado o del que se descargó un documento; <c>null</c> en listados y estadísticas.</summary>
    public Guid? ProcedureId { get; set; }

    /// <summary>Cliente hijo dueño del trámite consultado; <c>null</c> en listados y estadísticas.</summary>
    public Guid? ProcedureTenantId { get; set; }

    /// <summary>Documento descargado o visualizado; <c>null</c> salvo en <c>network.attachments.download</c>.</summary>
    public Guid? AttachmentId { get; set; }

    /// <summary><c>ok</c> | <c>forbidden</c> | <c>not_found</c>.</summary>
    public string Result { get; set; } = string.Empty;

    /// <summary>Convención A5 del checklist; nunca cambia (la tabla es append-only).</summary>
    public long RowVersion { get; set; }
}
