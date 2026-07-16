namespace Flit.Admin.Domain.OtClientProcedures;

/// <summary>Resumen de trámite de cliente visible para OT admin (HU #10217).</summary>
public sealed class OtClientProcedure
{
    public Guid Id { get; init; }

    public Guid ClientTenantId { get; init; }

    public Guid ProcedureTypeId { get; init; }

    public string ProcedureTypeName { get; init; } = string.Empty;

    public string ClientTenantName { get; init; } = string.Empty;

    public string ReferenceNumber { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    /// <summary>
    /// Feature #10587 / HU #10785 — sub-estado interno de la ruta de placa, ortogonal al <see cref="Status"/>
    /// (que permanece en 'entregado'): <c>null</c> (sin ruta de placa), <c>preasignado</c> (esperando placa)
    /// o <c>asignado</c> (placa registrada). Gobierna las acciones del OT (Asignar/Revocar placa).
    /// </summary>
    public string? PlateFlowStatus { get; init; }

    public Guid? TransitOfficeId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? SubmittedAt { get; init; }

    /// <summary>HU #10536 — trámite marcado como prioritario: se ordena con primacía en la bandeja del OT.</summary>
    public bool Prioritario { get; init; }
}
