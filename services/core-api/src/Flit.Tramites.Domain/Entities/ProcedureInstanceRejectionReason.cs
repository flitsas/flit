namespace Flit.Tramites.Domain.Entities;

/// <summary>
/// Causal del catálogo marcada por el organismo en un evento de rechazo concreto.
///
/// <para>Se cuelga de <see cref="StatusHistoryId"/> y no del trámite porque un expediente puede
/// rechazarse varias veces (ciclos de subsanación): las causales pertenecen al EVENTO. Colgarlas
/// del trámite mezclaría los ciclos y no permitiría distinguir «lo rechacé por A, se subsanó, y
/// ahora lo rechazo por B».</para>
///
/// <para>Complementa —no reemplaza— al texto libre de
/// <see cref="ProcedureInstanceStatusHistory.Reason"/>: la causal dice QUÉ falló (agregable en el
/// reporte), la observación dice CÓMO corregirlo (contexto para quien subsana).</para>
/// </summary>
public sealed class ProcedureInstanceRejectionReason
{
    public Guid Id { get; set; }

    public Guid TenantId { get; set; }

    public Guid ProcedureInstanceId { get; set; }

    /// <summary>
    /// Evento de rechazo (<c>entregado → rechazado</c>) al que pertenece la causal. Nullable por
    /// defensa: si alguna vez se registrara una causal fuera de una transición, la fila no se pierde.
    /// </summary>
    public Guid? StatusHistoryId { get; set; }

    public Guid RejectionReasonId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }
}
