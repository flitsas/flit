using System.Text.Json;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarLog;

/// <summary>
/// Página del LOG QX con eco de paginación. Es la forma que se serializa al cliente (contrato
/// <c>LogQxPage</c> en OpenAPI); la construye <see cref="ConsultarLogQuipuxHandler"/> a partir del
/// read-model del repositorio.
/// </summary>
public sealed record ConsultarLogQuipuxResult(
    IReadOnlyList<QuipuxLogEntryView> Data,
    int TotalCount,
    int Page,
    int PageSize);

/// <summary>
/// Una radicación con su línea de tiempo, ya proyectada para el cliente. Espeja
/// <c>tramites.quipux_submissions</c> enriquecida (referencia, tipo, razón social, placa) más el
/// estado final y los códigos QX; el paso a paso va en <see cref="Events"/>.
/// </summary>
public sealed record QuipuxLogEntryView(
    Guid Id,
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string ProcedureTypeName,
    string ClientTenantName,
    string? Plate,
    string DocumentName,
    string? DivipoCode,
    string Status,
    int Attempts,
    int PollCount,
    int? QxRegisterCode,
    int? QxProcedureCode,
    string? RejectionReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RegisteredAt,
    DateTimeOffset? LastPolledAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<QuipuxLogEventView> Events);

/// <summary>
/// Un evento de la línea de tiempo. <see cref="Detail"/> es el jsonb sanitizado tal cual se persistió
/// (o <c>null</c> = "sin payload disponible"); <see cref="DurationMs"/>, <see cref="Origin"/> y
/// <see cref="ResponseCode"/> se extraen de ese detail cuando existen (los eventos previos a la
/// instrumentación de la HU los traen en <c>null</c> → la UI muestra "sin dato"). El
/// <see cref="Origin"/> cae a una derivación por etapa cuando el detail no lo trae.
/// </summary>
public sealed record QuipuxLogEventView(
    string Stage,
    string Outcome,
    JsonElement? Detail,
    long? DurationMs,
    string? Origin,
    int? ResponseCode,
    Guid? CorrelationId,
    DateTimeOffset OccurredAt);
