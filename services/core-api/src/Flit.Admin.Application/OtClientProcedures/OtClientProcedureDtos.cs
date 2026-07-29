using Flit.Admin.Domain.OtClientProcedures;

namespace Flit.Admin.Application.OtClientProcedures;

public sealed class OtClientProcedureResponse
{
    public Guid Id { get; init; }

    public Guid ClientTenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public string ProcedureTypeName { get; init; } = string.Empty;

    public string ClientTenantName { get; init; } = string.Empty;

    public string ReferenceNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    /// <summary>Feature #10587 / HU #10785 — sub-estado interno de placa (null | preasignado | asignado).</summary>
    public string? PlateFlowStatus { get; init; }

    /// <summary>HU #10804 (Feature #10587) — estado del SOAT (soat_estado): null | unknown | vencido | vigente.
    /// El frontend oculta Aprobar/Rechazar salvo ruta estándar o placa asignada con SOAT vigente.</summary>
    public string? SoatEstado { get; init; }

    /// <summary>HU #10805 (Feature #10587) — dígito de preferencia de placa (0-9). Guía para el OT
    /// al asignar; no obliga (puede asignar una placa que termine en otro dígito).</summary>
    public string? PlatePreferredLastDigit { get; init; }

    /// <summary>Check opcional del gestor; badge en dashboard OT solo si PlateFlowStatus = terminado.</summary>
    public bool SoatPagado { get; init; }

    /// <summary>Check opcional del gestor; badge en dashboard OT solo si PlateFlowStatus = terminado.</summary>
    public bool ImpuestoDepartamentalPagado { get; init; }

    public Guid? TransitOfficeId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    /// <summary>HU #10536 — trámite prioritario: se ordena con primacía en la bandeja del OT.</summary>
    public bool Prioritario { get; init; }
}

public sealed class RejectOtClientProcedureRequest
{
    public string Reason { get; init; } = string.Empty;

    /// <summary>
    /// HU #10871 (AC1) — checklist de ítems subsanables (opcional). Con al menos un ítem, el trámite
    /// pasa a <c>subsanacion</c> (observación) en vez de <c>rechazado</c> (rechazo definitivo, el
    /// comportamiento previo a esta HU cuando no se envían ítems).
    /// </summary>
    public IReadOnlyList<OtProcedureObservationItem>? Items { get; init; }
}

/// <summary>
/// Body OPCIONAL de POST .../client-procedures/{id}/approve (ADR-0036 §D9, HU #10916). El aprobador
/// solo lo envía para elegir el mandatario cuando hay varios y ninguno cotejó (subsana el 409
/// <c>mandatario_requerido</c>). Sin body, o con <c>MandateSignerId</c> nulo, la aprobación resuelve el
/// firmante automáticamente (uno solo o cotejo por usuario).
/// </summary>
public sealed class ApproveOtClientProcedureRequest
{
    public Guid? MandateSignerId { get; init; }
}

internal static class OtClientProcedureMapper
{
    public static OtClientProcedureResponse ToResponse(Domain.OtClientProcedures.OtClientProcedure procedure) =>
        new()
        {
            Id = procedure.Id,
            ClientTenantId = procedure.ClientTenantId,
            ProcedureTypeId = procedure.ProcedureTypeId,
            ProcedureTypeName = procedure.ProcedureTypeName,
            ClientTenantName = procedure.ClientTenantName,
            ReferenceNumber = procedure.ReferenceNumber,
            Status = procedure.Status,
            PlateFlowStatus = procedure.PlateFlowStatus,
            SoatEstado = procedure.SoatEstado,
            PlatePreferredLastDigit = procedure.PlatePreferredLastDigit,
            SoatPagado = procedure.SoatPagado,
            ImpuestoDepartamentalPagado = procedure.ImpuestoDepartamentalPagado,
            TransitOfficeId = procedure.TransitOfficeId,
            CreatedAt = procedure.CreatedAt,
            SubmittedAt = procedure.SubmittedAt,
            Prioritario = procedure.Prioritario,
        };
}
