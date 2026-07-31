namespace Flit.Modules.Quipux.Domain.LogQx;

/// <summary>
/// Filtro de búsqueda del LOG QX (HU #10793). Los tres ejes son excluyentes-opcionales: se acota por
/// el que venga (placa, id de trámite o radicado). Si los tres son <c>null</c>, se listan todas las
/// radicaciones paginadas (modo diagnóstico/navegación). La normalización de paginación la hace el
/// handler; aquí <see cref="Page"/>/<see cref="PageSize"/> ya llegan saneados.
/// </summary>
public sealed record QuipuxLogQuery(
    string? Placa,
    Guid? ProcedureInstanceId,
    string? Radicado,
    int Page,
    int PageSize);

/// <summary>
/// Una radicación (<c>tramites.quipux_submissions</c>) enriquecida con datos del trámite y con su
/// bitácora completa de eventos (<c>tramites.quipux_submission_events</c>). Es el read-model que
/// alimenta la línea de tiempo del LOG QX: el estado final y los códigos QX viven en la cabecera; el
/// paso a paso, en <see cref="Events"/>.
/// </summary>
public sealed class QuipuxLogEntry
{
    public Guid Id { get; init; }

    public Guid ProcedureInstanceId { get; init; }

    public string ReferenceNumber { get; init; } = string.Empty;

    public string ProcedureTypeName { get; init; } = string.Empty;

    public string ClientTenantName { get; init; } = string.Empty;

    /// <summary>Placa proyectada de <c>procedure_instance_field_values</c>; null en trámites por VIN.</summary>
    public string? Plate { get; init; }

    public string DocumentName { get; init; } = string.Empty;

    public string? DivipoCode { get; init; }

    /// <summary>Estado final: <c>pendiente | registrado | aprobado | rechazado | fallido</c>.</summary>
    public string Status { get; init; } = string.Empty;

    public int Attempts { get; init; }

    public int PollCount { get; init; }

    public int? QxRegisterCode { get; init; }

    public int? QxProcedureCode { get; init; }

    public string? RejectionReason { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? RegisteredAt { get; init; }

    public DateTimeOffset? LastPolledAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Eventos de la radicación, del más viejo al más nuevo (orden de la línea de tiempo).</summary>
    public IReadOnlyList<QuipuxLogEvent> Events { get; init; } = [];
}

/// <summary>
/// Un evento de la bitácora, tal cual se persistió. <see cref="Detail"/> es el jsonb SANITIZADO
/// (nunca request/response crudos ni PII): puede ser <c>null</c> en registros históricos parciales,
/// que la UI muestra como "sin payload disponible". La extracción de <c>duration_ms</c>/<c>origen</c>
/// desde el detail la hace la capa de aplicación al proyectar la vista.
/// </summary>
public sealed class QuipuxLogEvent
{
    public string Stage { get; init; } = string.Empty;

    public string Outcome { get; init; } = string.Empty;

    public string? Detail { get; init; }

    public Guid? CorrelationId { get; init; }

    public DateTimeOffset OccurredAt { get; init; }
}

/// <summary>Página de radicaciones del LOG QX con el total para el eco de paginación.</summary>
public sealed record QuipuxLogPage(IReadOnlyList<QuipuxLogEntry> Entries, int TotalCount);
