using System.Text.Json;

namespace Flit.Modules.Quipux.Application.UseCases.ConsultarTrazabilidad;

/// <summary>Naturaleza de una entrada de la línea de tiempo.</summary>
public static class QuipuxHitoTipo
{
    /// <summary>Algo pasó: se consolidó, se radicó, falló, la secretaría decidió.</summary>
    public const string Hito = "hito";

    /// <summary>
    /// Una racha de consultas que no cambiaron nada, colapsada. La interfaz la dibuja distinta a
    /// propósito: es ruido de fondo y debe leerse como tal antes de leer una sola palabra.
    /// </summary>
    public const string Sondeo = "sondeo";
}

/// <summary>
/// Una entrada de la línea de tiempo. Con <c>Tipo = sondeo</c>, <c>OccurredAt</c> y <c>Hasta</c>
/// delimitan la ventana y <c>Consultas</c> dice cuántas se colapsaron; con <c>Tipo = hito</c>,
/// <c>Hasta</c> y <c>Consultas</c> van en <c>null</c> y el evento es uno solo.
/// </summary>
public sealed record QuipuxHitoView(
    string Tipo,
    string Stage,
    string Outcome,
    DateTimeOffset OccurredAt,
    DateTimeOffset? Hasta,
    long? DurationMs,
    int? Codigo,
    int? EstadoTramite,
    string? Mensaje,
    Guid? CorrelationId,
    int? Consultas,
    long? DuracionMediaMs);

/// <summary>Una radicación hermana del mismo trámite, para la tira de intentos.</summary>
public sealed record QuipuxHermanaView(
    Guid Id,
    int Intento,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>Cabecera de la radicación en la pantalla de trazabilidad.</summary>
public sealed record QuipuxRadicacionView(
    Guid Id,
    Guid ProcedureInstanceId,
    string ReferenceNumber,
    string? Plate,
    string ProcedureTypeName,
    string ClientTenantName,
    string TransitOfficeName,
    string? DivipoCode,
    string DocumentoQx,
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
    int Intento,
    int TotalIntentos,
    IReadOnlyList<QuipuxHermanaView> Hermanas);

/// <summary>Respuesta de la pestaña «Hitos» (contrato <c>LogQxHitos</c> en OpenAPI).</summary>
public sealed record ConsultarHitosQuipuxResult(
    QuipuxRadicacionView Radicacion,
    IReadOnlyList<QuipuxHitoView> Hitos);

/// <summary>
/// Un evento del log completo. <c>Detail</c> es el jsonb sanitizado y además ENMASCARADO, o
/// <c>null</c> = «sin payload disponible».
/// </summary>
public sealed record QuipuxEventoView(
    string Stage,
    string Outcome,
    JsonElement? Detail,
    long? DurationMs,
    string? Origin,
    int? ResponseCode,
    Guid? CorrelationId,
    DateTimeOffset OccurredAt);

/// <summary>
/// Página de la pestaña «Log completo». <c>OcultosSinNovedad</c> es cuántos sondeos se dejaron
/// fuera: sin ese número, ver 5 filas de una radicación de 1.065 eventos parece pérdida de datos.
/// </summary>
public sealed record ConsultarEventosQuipuxResult(
    IReadOnlyList<QuipuxEventoView> Data,
    int TotalCount,
    int Page,
    int PageSize,
    int OcultosSinNovedad,
    int TotalEventos);
