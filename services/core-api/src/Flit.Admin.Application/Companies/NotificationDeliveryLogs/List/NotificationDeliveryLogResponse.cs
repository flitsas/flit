namespace Flit.Admin.Application.Companies.NotificationDeliveryLogs.List;

/// <summary>
/// Un registro de la bitácora consultable (HU #11363, AC1/AC2). <see cref="Result"/> solo puede ser
/// <c>"enviado"</c> o <c>"fallido"</c> (CHECK de BD, DDL 64) — nunca <c>"entregado"</c>: el
/// vocabulario mismo impide afirmar que el destinatario recibió el correo (AC2), sea cual sea el
/// canal. <see cref="FailureReason"/> es la causa GENÉRICA del catálogo cerrado de
/// <c>EmailSendOutcome</c> (nunca el texto crudo del proveedor ni un secreto — AC4).
/// </summary>
public sealed record NotificationDeliveryLogResponse(
    Guid Id,
    string TemplateKey,
    string Channel,
    string Recipient,
    string Result,
    string? FailureReason,
    int DurationMs,
    DateTimeOffset OccurredAt);
