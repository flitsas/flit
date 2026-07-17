namespace Flit.Modules.Quipux.Domain.Consola;

/// <summary>
/// Una fila de la cola Quipux tal como la ve la consola del SuperAdmin (HU #10774): la radicación
/// (<c>tramites.quipux_submissions</c>) enriquecida con los datos del trámite que la hacen legible
/// —número de referencia, tipo de trámite y razón social del cliente— sin obligar al front a cruzar
/// tablas. Es un read model: no tiene comportamiento, se materializa desde un JOIN.
/// </summary>
public sealed record QuipuxSubmissionQueueItem
{
    public required Guid Id { get; init; }

    public required Guid ProcedureInstanceId { get; init; }

    /// <summary>Número de referencia del trámite (<c>procedure_instances.reference_number</c>).</summary>
    public required string ReferenceNumber { get; init; }

    /// <summary>Nombre del tipo de trámite (<c>procedure_types.name</c>).</summary>
    public required string ProcedureTypeName { get; init; }

    /// <summary>Razón social del cliente dueño del trámite (<c>identity.tenants.legal_name</c>).</summary>
    public required string ClientTenantName { get; init; }

    /// <summary>El <c>documento</c> enviado a Quipux; llave de correlación estable entre reintentos.</summary>
    public required string DocumentName { get; init; }

    /// <summary>Ver <c>QuipuxSubmissionEstado</c>: pendiente | registrado | aprobado | rechazado | fallido.</summary>
    public required string Status { get; init; }

    public int Attempts { get; init; }

    public int PollCount { get; init; }

    /// <summary>Último <c>codigo</c> del registro (81 = radicado).</summary>
    public int? QxRegisterCode { get; init; }

    /// <summary>Último <c>estadoTramite.codigo</c> de la consulta (2 aprobado, 3 rechazado).</summary>
    public int? QxProcedureCode { get; init; }

    /// <summary>Descripción del rechazo de Quipux, si lo hubo.</summary>
    public string? RejectionReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RegisteredAt { get; init; }

    public DateTimeOffset? LastPolledAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

/// <summary>Una página de la cola: los ítems y el total para paginar en el cliente.</summary>
public sealed record QuipuxSubmissionQueuePage(
    IReadOnlyList<QuipuxSubmissionQueueItem> Items,
    int TotalCount);
